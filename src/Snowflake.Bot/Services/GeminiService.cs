using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Snowflake.Bot.Configuration;

namespace Snowflake.Bot.Services;

/// <summary>
/// Chatbot basado en la API gratuita de Google Gemini.
/// Mantiene una conversación compartida por servidor: todos los usuarios del
/// servidor continúan el mismo contexto, incluso desde distintos canales.
/// </summary>
public sealed class GeminiService
{
    private const string EndpointBase =
        "https://generativelanguage.googleapis.com/v1beta/models/";
    private const int MaxSolicitudesPorServidor = 2;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly IOptionsMonitor<GeminiOptions> _options;
    private readonly ConcurrentDictionary<ulong, Conversacion> _conversaciones = new();

    // Message ID -> guild ID. Solo se guardan mensajes producidos por este chatbot.
    private readonly ConcurrentDictionary<ulong, ulong> _mensajesGenerados = new();

    public GeminiService(IHttpClientFactory httpFactory, IOptionsMonitor<GeminiOptions> options)
    {
        _httpFactory = httpFactory;
        _options = options;
    }

    /// <summary>Nombre del modelo activo (GEMINI_MODEL tiene prioridad).</summary>
    public string ModeloActivo
    {
        get
        {
            var envModel = Environment.GetEnvironmentVariable("GEMINI_MODEL")?.Trim();
            return !string.IsNullOrEmpty(envModel) ? envModel : _options.CurrentValue.Model;
        }
    }

    /// <summary>
    /// Envía un mensaje a la conversación compartida del servidor y devuelve la
    /// respuesta generada. Las solicitudes del mismo servidor se serializan para
    /// conservar el orden cuando varios usuarios escriben a la vez.
    /// </summary>
    public async Task<string> PreguntarAsync(
        ulong guildId,
        string autor,
        string texto,
        CancellationToken ct = default)
    {
        texto = texto.Trim();
        autor = string.IsNullOrWhiteSpace(autor) ? "Usuario" : autor.Trim();
        var opts = _options.CurrentValue;

        if (string.IsNullOrWhiteSpace(texto))
            throw new GeminiException("El mensaje no puede estar vacío.");

        if (texto.Length > opts.MaxInputLength)
            throw new GeminiException($"El mensaje supera el máximo de {opts.MaxInputLength} caracteres.");

        var clave = Environment.GetEnvironmentVariable("GEMINI_API_KEY")?.Trim();
        if (string.IsNullOrEmpty(clave))
            throw new GeminiException("Falta la variable de entorno GEMINI_API_KEY.");

        var conversacion = _conversaciones.GetOrAdd(guildId, _ => new Conversacion());
        if (!conversacion.IntentarReservar())
            throw new GeminiBusyException(
                "Ya hay dos solicitudes de chat pendientes en este servidor.");

        var entrada = $"[{autor}] {texto}";
        try
        {
            await conversacion.Puerta.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                PrepararParaNuevoMensaje(conversacion.Mensajes, opts.MaxHistoryTurns);
                conversacion.Mensajes.Add(("user", entrada));

                var cuerpo = ConstruirCuerpo(conversacion.Mensajes, opts);
                var url = $"{EndpointBase}{ModeloActivo}:generateContent?key={Uri.EscapeDataString(clave)}";

                var http = _httpFactory.CreateClient("Gemini");
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(cuerpo, MediaTypeHeaderValue.Parse("application/json"))
                };

                using var resp = await EnviarAsync(http, req, ct).ConfigureAwait(false);
                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

                // Errores de la API: { "error": { "message": "...", "status": "..." } }
                if (!resp.IsSuccessStatusCode)
                {
                    var mensaje = json.RootElement.TryGetProperty("error", out var err)
                        && err.TryGetProperty("message", out var msg)
                            ? msg.GetString() ?? $"HTTP {(int)resp.StatusCode}"
                            : $"HTTP {(int)resp.StatusCode}";
                    throw new GeminiException($"Gemini respondió con error: {mensaje}");
                }

                var respuesta = ExtraerTexto(json.RootElement);
                if (string.IsNullOrWhiteSpace(respuesta))
                    throw new GeminiException("Gemini no devolvió respuesta (posible filtro de seguridad).");

                conversacion.Mensajes.Add(("model", respuesta));
                RecortarHistorial(conversacion.Mensajes, opts.MaxHistoryTurns);
                return respuesta;
            }
            catch
            {
                // No conservamos un mensaje que Gemini no llegó a contestar.
                if (conversacion.Mensajes.Count > 0
                    && conversacion.Mensajes[^1] is { Role: "user" } ultimo
                    && ultimo.Text == entrada)
                {
                    conversacion.Mensajes.RemoveAt(conversacion.Mensajes.Count - 1);
                }
                throw;
            }
            finally
            {
                conversacion.Puerta.Release();
            }
        }
        finally
        {
            conversacion.Liberar();
        }
    }

    /// <summary>
    /// Borra la conversación compartida del servidor y deja de reconocer sus
    /// mensajes anteriores como mensajes activos del chatbot.
    /// </summary>
    public bool Limpiar(ulong guildId)
    {
        var habiaConversacion = _conversaciones.TryRemove(guildId, out _);

        foreach (var mensaje in _mensajesGenerados)
        {
            if (mensaje.Value == guildId)
                _mensajesGenerados.TryRemove(mensaje.Key, out _);
        }

        return habiaConversacion;
    }

    public bool TieneHistorial(ulong guildId) => _conversaciones.ContainsKey(guildId);

    /// <summary>Marca un mensaje del bot como respuesta generada por este chatbot.</summary>
    public void RegistrarMensajeGenerado(ulong messageId, ulong guildId)
        => _mensajesGenerados[messageId] = guildId;

    /// <summary>
    /// Comprueba si un mensaje es uno de los mensajes generados por el chatbot y
    /// devuelve el servidor al que pertenece su conversación.
    /// </summary>
    public bool TryObtenerGuildDeMensajeGenerado(ulong messageId, out ulong guildId)
        => _mensajesGenerados.TryGetValue(messageId, out guildId);

    // ------ API HTTP y JSON ------

    private static async Task<HttpResponseMessage> EnviarAsync(
        HttpClient http,
        HttpRequestMessage request,
        CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new GeminiException("No pude contactar con Gemini: " + ex.Message, ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new GeminiException("Gemini tardó demasiado en responder (timeout).", ex);
        }
    }

    private static string ExtraerTexto(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidatos) || candidatos.GetArrayLength() == 0)
            return string.Empty;

        var primero = candidatos[0];
        if (!primero.TryGetProperty("content", out var contenido)) return string.Empty;
        if (!contenido.TryGetProperty("parts", out var partes)) return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var parte in partes.EnumerateArray())
        {
            if (parte.TryGetProperty("text", out var textoEl))
            {
                var texto = textoEl.GetString();
                if (!string.IsNullOrEmpty(texto)) sb.Append(texto);
            }
        }
        return sb.ToString();
    }

    private static void PrepararParaNuevoMensaje(
        List<(string Role, string Text)> mensajes,
        int turnosPorRol)
    {
        var max = MaximoMensajes(turnosPorRol);

        // Quitamos turnos completos antes de añadir un nuevo mensaje para que
        // el historial nunca empiece por un mensaje con role=model.
        while (mensajes.Count >= max && mensajes.Count >= 2)
            mensajes.RemoveRange(0, 2);
    }

    private static void RecortarHistorial(
        List<(string Role, string Text)> mensajes,
        int turnosPorRol)
    {
        var max = MaximoMensajes(turnosPorRol);
        while (mensajes.Count > max && mensajes.Count >= 2)
            mensajes.RemoveRange(0, 2);
    }

    private static int MaximoMensajes(int turnosPorRol)
        => Math.Max(1, turnosPorRol) * 2;

    private static string ConstruirCuerpo(
        List<(string Role, string Text)> mensajes,
        GeminiOptions opts)
    {
        var contenido = mensajes
            .Select(m => new GeminiContent(m.Role, new List<GeminiPart> { new(m.Text) }))
            .ToList();

        var sistema = string.IsNullOrWhiteSpace(opts.SystemPrompt)
            ? null
            : new GeminiSystemInstruction(new List<GeminiPart> { new(opts.SystemPrompt) });

        var req = new GeminiRequest(
            contenido,
            sistema,
            new GeminiGenerationConfig(opts.Temperature, opts.MaxOutputTokens),
            new List<GeminiTool> { new(new GeminiGoogleSearch()) });

        return JsonSerializer.Serialize(req, JsonOpts);
    }

    // ------ estado por servidor ------

    private sealed class Conversacion
    {
        public List<(string Role, string Text)> Mensajes { get; } = new();
        public SemaphoreSlim Puerta { get; } = new(1, 1);

        private int _solicitudesReservadas;

        public bool IntentarReservar()
        {
            while (true)
            {
                var actuales = Volatile.Read(ref _solicitudesReservadas);
                if (actuales >= MaxSolicitudesPorServidor)
                    return false;

                if (Interlocked.CompareExchange(
                        ref _solicitudesReservadas,
                        actuales + 1,
                        actuales) == actuales)
                {
                    return true;
                }
            }
        }

        public void Liberar() => Interlocked.Decrement(ref _solicitudesReservadas);
    }

    // ------ DTOs serializados como JSON ------

    private sealed record GeminiPart(
        [property: JsonPropertyName("text")] string Text);

    private sealed record GeminiContent(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("parts")] List<GeminiPart> Parts);

    private sealed record GeminiSystemInstruction(
        [property: JsonPropertyName("parts")] List<GeminiPart> Parts);

    private sealed record GeminiGenerationConfig(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("maxOutputTokens")] int MaxOutputTokens);

    private sealed record GeminiGoogleSearch;

    private sealed record GeminiTool(
        [property: JsonPropertyName("googleSearch")] GeminiGoogleSearch GoogleSearch);

    private sealed record GeminiRequest(
        [property: JsonPropertyName("contents")] List<GeminiContent> Contents,
        [property: JsonPropertyName("systemInstruction")] GeminiSystemInstruction? SystemInstruction,
        [property: JsonPropertyName("generationConfig")] GeminiGenerationConfig GenerationConfig,
        [property: JsonPropertyName("tools")] List<GeminiTool> Tools);
}

/// <summary>
/// Error de comunicación con la API de Gemini.
/// </summary>
public class GeminiException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Indica que el servidor ya tiene el máximo de solicitudes de chat pendientes.</summary>
public sealed class GeminiBusyException(string message) : GeminiException(message);
