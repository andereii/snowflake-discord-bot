using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Snowflake.Bot.Configuration;
using Snowflake.Bot.Services.AiCommands;
using Snowflake.Bot.Services.Settings;

namespace Snowflake.Bot.Services;

/// <summary>
/// Resultado de un turno de chat con la IA.
/// </summary>
public sealed record AiChatOutcome
{
    /// <summary>Texto final generado por el modelo (null si quedó pendiente una confirmación).</summary>
    public string? Texto { get; init; }

    /// <summary>Comandos ya ejecutados en este turno (para mostrar su output en embeds).</summary>
    public List<AiCommandResult> Comandos { get; init; } = [];

    /// <summary>Comando destructivo pendiente de confirmación con botones (si aplica).</summary>
    public PendingCommand? Pendiente { get; init; }

    public bool HayPendiente => Pendiente is not null;
}

/// <summary>
/// Comando destructivo que el bot no ejecutará hasta que el usuario que lo
/// pidió pulse "Aceptar" en la confirmación (o se agote el timeout de 15 s).
/// </summary>
public sealed record PendingCommand(
    string Token,
    string ToolName,
    JsonObject Args,
    string CallId,
    JsonNode FunctionCallItem,
    string DescripcionComando);

/// <summary>Hay una confirmación de comando pendiente en el servidor.</summary>
public sealed class DeepSeekConfirmationPendingException(string message) : Exception(message);

/// <summary>
/// Chatbot basado en la API de DeepSeek (deepseek-v4-flash por defecto),
/// usando la Responses API (formato OpenAI). Mantiene una conversación
/// compartida por servidor, puede buscar en internet (web_search con
/// tool_choice "auto") y puede ejecutar comandos del bot desde el chat
/// (tools de AiCommandExecutor). El system prompt va SIEMPRE en inglés.
/// </summary>
public sealed partial class DeepSeekService
{
    private const string Endpoint = "https://api.deepseek.com/responses";

    // Tool nativa de DeepSeek para búsqueda web (ver api-docs.deepseek.com).
    private const string WebSearchTool = "web_search";

    /// <summary>Iteraciones máximas del bucle tool-call por turno.</summary>
    private const int MaxIteraciones = 5;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly IOptionsMonitor<DeepSeekOptions> _options;
    private readonly GuildSettingsService _settings;
    private readonly AiCommandExecutor _executor;
    private readonly ConcurrentDictionary<ulong, Conversacion> _conversaciones = new();

    // Message ID -> guild ID. Solo se guardan mensajes producidos por este chatbot.
    private readonly ConcurrentDictionary<ulong, ulong> _mensajesGenerados = new();

    // Toggle de cháchara espontánea en memoria (para no tocar la BD en cada mensaje).
    private readonly ConcurrentDictionary<ulong, bool> _espontaneoHabilitado = new();

    // Estado del contador espontáneo por servidor.
    private readonly ConcurrentDictionary<ulong, EstadoEspontaneo> _espontaneo = new();

    public DeepSeekService(
        IHttpClientFactory httpFactory,
        IOptionsMonitor<DeepSeekOptions> options,
        GuildSettingsService settings,
        AiCommandExecutor executor)
    {
        _httpFactory = httpFactory;
        _options = options;
        _settings = settings;
        _executor = executor;
    }

    /// <summary>Nombre del modelo activo (DEEPSEEK_MODEL tiene prioridad).</summary>
    public string ModeloActivo
    {
        get
        {
            var envModel = Environment.GetEnvironmentVariable("DEEPSEEK_MODEL")?.Trim();
            return !string.IsNullOrEmpty(envModel) ? envModel : _options.CurrentValue.Model;
        }
    }

    /// <summary>
    /// Envía un mensaje a la conversación compartida del servidor. El modelo
    /// puede llamar tools (búsqueda web y comandos del bot) según la
    /// configuración del servidor. Si propone un comando destructivo, devuelve
    /// <see cref="AiChatOutcome.HayPendiente"/> sin ejecutarlo.
    /// </summary>
    public async Task<AiChatOutcome> PreguntarAsync(
        AiCommandContext ctx,
        string autor,
        string texto,
        CancellationToken ct = default)
    {
        texto = texto.Trim();
        autor = string.IsNullOrWhiteSpace(autor) ? "User" : autor.Trim();
        var opts = _options.CurrentValue;

        if (string.IsNullOrWhiteSpace(texto))
            throw new DeepSeekException("The message cannot be empty.");

        if (texto.Length > opts.MaxInputLength)
            throw new DeepSeekException($"The message exceeds the maximum of {opts.MaxInputLength} characters.");

        var clave = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")?.Trim();
        if (string.IsNullOrEmpty(clave))
            throw new DeepSeekException("DEEPSEEK_API_KEY environment variable is missing.");

        var cfg = await _settings.GetAsync(ctx.Guild.Id).ConfigureAwait(false);
        var conBusqueda = cfg.AiWebSearchEnabled;
        var conComandos = cfg.AiCommandsEnabled;

        var conversacion = _conversaciones.GetOrAdd(ctx.Guild.Id, _ => new Conversacion());
        if (!conversacion.IntentarReservar(opts.MaxConcurrentPerGuild))
            throw new DeepSeekBusyException(
                "Too many chat requests are already pending on this server.");

        var entrada = $"[{autor}] {texto}";
        var itemUsuario = CrearItemUsuario(entrada);
        try
        {
            await conversacion.Puerta.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (conversacion.ConfirmacionPendiente)
                    throw new DeepSeekConfirmationPendingException(
                        "There is a pending command confirmation on this server.");

                RecortarHistorial(conversacion.Historial, opts.MaxHistoryTurns);

                var input = new List<JsonNode>(conversacion.Historial) { itemUsuario };
                conversacion.Historial.Add(itemUsuario);

                var resultado = await BucleAsync(
                    conversacion, input, ctx, opts, conBusqueda, conComandos, ct).ConfigureAwait(false);

                RecortarHistorial(conversacion.Historial, opts.MaxHistoryTurns);
                return resultado;
            }
            catch
            {
                // No conservamos un mensaje que el modelo no llegó a contestar.
                conversacion.Historial.Remove(itemUsuario);
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
    /// Reanuda la conversación tras la resolución de una confirmación: añade
    /// el resultado de la tool (ejecutada o rechazada) y deja que el modelo
    /// termine la respuesta.
    /// </summary>
    public async Task<AiChatOutcome> ReanudarToolAsync(
        AiCommandContext ctx, PendingCommand pendiente, string resultadoTexto, CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        var conversacion = _conversaciones.GetOrAdd(ctx.Guild.Id, _ => new Conversacion());

        await conversacion.Puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            conversacion.ConfirmacionPendiente = false;

            // BucleAsync ya añade FunctionCallItem al historial al recibirlo del modelo.
            // Si no estaba, lo agregamos; si ya existe, no duplicamos para evitar error 'Duplicate call_id'.
            var yaTieneCall = conversacion.Historial.Any(h =>
                h is JsonObject obj
                && obj["type"]?.GetValue<string>() == "function_call"
                && obj["call_id"]?.GetValue<string>() == pendiente.CallId);

            if (!yaTieneCall)
                conversacion.Historial.Add(pendiente.FunctionCallItem);

            var yaTieneOutput = conversacion.Historial.Any(h =>
                h is JsonObject obj
                && obj["type"]?.GetValue<string>() == "function_call_output"
                && obj["call_id"]?.GetValue<string>() == pendiente.CallId);

            if (!yaTieneOutput)
                conversacion.Historial.Add(CrearItemToolOutput(pendiente.CallId, resultadoTexto));

            var cfg = await _settings.GetAsync(ctx.Guild.Id).ConfigureAwait(false);
            var input = new List<JsonNode>(conversacion.Historial);
            var resultado = await BucleAsync(
                conversacion, input, ctx, opts, cfg.AiWebSearchEnabled, cfg.AiCommandsEnabled, ct).ConfigureAwait(false);

            RecortarHistorial(conversacion.Historial, opts.MaxHistoryTurns);
            return resultado;
        }
        finally
        {
            conversacion.Puerta.Release();
        }
    }

    /// <summary>Marca que una confirmación quedó pendiente en este servidor.</summary>
    public void MarcarPendiente(ulong guildId)
    {
        if (_conversaciones.TryGetValue(guildId, out var conv))
            conv.ConfirmacionPendiente = true;
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

    // ------ bucle de tools ------

    /// <summary>
    /// Bucle principal de un turno: llama a la API y, si el modelo pide tools,
    /// las ejecuta (salvo las destructivas, que interrumpen con pendiente) y
    /// continúa hasta obtener texto final o agotar iteraciones.
    /// </summary>
    private async Task<AiChatOutcome> BucleAsync(
        Conversacion conversacion,
        List<JsonNode> input,
        AiCommandContext ctx,
        DeepSeekOptions opts,
        bool conBusqueda,
        bool conComandos,
        CancellationToken ct)
    {
        var ejecutados = new List<AiCommandResult>();

        for (var iteracion = 0; iteracion < MaxIteraciones; iteracion++)
        {
            var (texto, salida, tools) = await LlamarAsync(input, opts, conBusqueda, conComandos, ct).ConfigureAwait(false);

            // Guardamos todo el output (message + tool calls) en el historial.
            conversacion.Historial.AddRange(salida);
            input.AddRange(salida);

            if (tools.Count == 0)
                return new AiChatOutcome { Texto = texto, Comandos = ejecutados };

            foreach (var (nombre, callId, argsJson, item) in tools)
            {
                JsonObject? args = null;
                try
                {
                    args = string.IsNullOrWhiteSpace(argsJson)
                        ? new JsonObject()
                        : JsonNode.Parse(argsJson) as JsonObject ?? new JsonObject();
                }
                catch (JsonException)
                {
                    args = new JsonObject();
                }

                var ejecucion = await _executor.EjecutarAsync(ctx, nombre, args).ConfigureAwait(false);

                if (ejecucion.Destructivo)
                {
                    // Comando destructivo: se detiene el turno y se pide
                    // confirmación con botones al usuario que lo solicitó.
                    conversacion.ConfirmacionPendiente = true;
                    var pendiente = new PendingCommand(
                        Token: Guid.NewGuid().ToString("N"),
                        ToolName: nombre,
                        Args: args,
                        CallId: callId,
                        FunctionCallItem: item,
                        DescripcionComando: ejecucion.DescripcionComando);
                    return new AiChatOutcome { Pendiente = pendiente, Comandos = ejecutados };
                }

                if (ejecucion.Resultado is { } resultado)
                {
                    ejecutados.Add(resultado);
                    var output = CrearItemToolOutput(callId, resultado.Texto);
                    conversacion.Historial.Add(output);
                    input.Add(output);
                }
            }
        }

        // Se agotaron las iteraciones: devolvemos el último texto disponible.
        return new AiChatOutcome { Texto = "…", Comandos = ejecutados };
    }

    // ------ API HTTP y JSON ------

    /// <summary>
    /// Llama a la Responses API y devuelve el texto final, los items de salida
    /// completos y las tool calls pedidas por el modelo.
    /// </summary>
    private async Task<(string Texto, List<JsonNode> Salida, List<(string Nombre, string CallId, string Args, JsonNode Item)> Tools)> LlamarAsync(
        List<JsonNode> input, DeepSeekOptions opts, bool conBusqueda, bool conComandos, CancellationToken ct)
    {
        var clave = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")?.Trim();
        if (string.IsNullOrEmpty(clave))
            throw new DeepSeekException("DEEPSEEK_API_KEY environment variable is missing.");

        var tools = conComandos
            ? _executor.Herramientas
                .Select(d => new DeepSeekTool("function", d.Nombre, d.Descripcion, d.Esquema))
                .ToList()
            : [];

        var cuerpo = ConstruirCuerpo2(input, opts, conBusqueda, tools);
        var http = _httpFactory.CreateClient("DeepSeek");

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(cuerpo, MediaTypeHeaderValue.Parse("application/json"))
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clave);

        using var resp = await EnviarAsync(http, req, ct).ConfigureAwait(false);
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            var mensaje = json.RootElement.TryGetProperty("error", out var err)
                && err.TryGetProperty("message", out var msg)
                    ? msg.GetString() ?? $"HTTP {(int)resp.StatusCode}"
                    : $"HTTP {(int)resp.StatusCode}";
            throw new DeepSeekException($"DeepSeek responded with an error: {mensaje}");
        }

        return ExtraerRespuesta(json.RootElement);
    }

    private static (string Texto, List<JsonNode> Salida, List<(string, string, string, JsonNode)> Tools) ExtraerRespuesta(JsonElement root)
    {
        var salida = new List<JsonNode>();
        var tools = new List<(string, string, string, JsonNode)>();
        var sb = new StringBuilder();

        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                var nodo = JsonNode.Parse(item.GetRawText())!;
                salida.Add(nodo);

                var tipo = item.TryGetProperty("type", out var tipoEl) ? tipoEl.GetString() : null;

                if (tipo == "function_call")
                {
                    var nombre = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                    var callId = item.TryGetProperty("call_id", out var callEl) ? callEl.GetString() ?? "" : "";
                    var args = item.TryGetProperty("arguments", out var argsEl) ? argsEl.GetString() ?? "" : "";
                    tools.Add((nombre, callId, args, nodo));
                }
                else if (tipo == "message" && item.TryGetProperty("content", out var content))
                {
                    if (content.ValueKind == JsonValueKind.String)
                    {
                        sb.Append(content.GetString());
                    }
                    else if (content.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var parte in content.EnumerateArray())
                        {
                            if (parte.TryGetProperty("text", out var textoEl))
                                sb.Append(textoEl.GetString());
                        }
                    }
                }
            }
        }

        return (sb.ToString(), salida, tools);
    }

    /// <summary>Item de input tipo "message" con rol user.</summary>
    private static JsonNode CrearItemUsuario(string texto) =>
        JsonNode.Parse(JsonSerializer.Serialize(new
        {
            type = "message",
            role = "user",
            content = texto
        }))!;

    /// <summary>Item de input tipo "function_call_output" con el resultado de una tool.</summary>
    private static JsonNode CrearItemToolOutput(string callId, string salida) =>
        JsonNode.Parse(JsonSerializer.Serialize(new
        {
            type = "function_call_output",
            call_id = callId,
            output = salida
        }))!;

    /// <summary>
    /// Mantiene el historial acotado a <paramref name="turnosPorRol"/> mensajes
    /// de usuario: se eliminan turnos completos por el principio, sin dejar
    /// items huérfanos (tool calls/outputs) al inicio.
    /// </summary>
    private static void RecortarHistorial(List<JsonNode> historial, int turnosPorRol)
    {
        var max = Math.Max(1, turnosPorRol);

        while (ContarUsuarios(historial) > max)
            historial.RemoveAt(0);

        while (historial.Count > 0 && !EsUsuario(historial[0]))
            historial.RemoveAt(0);

        var sanitizado = SanitizarInput(historial);
        if (sanitizado.Count != historial.Count)
        {
            historial.Clear();
            historial.AddRange(sanitizado);
        }
    }

    /// <summary>
    /// Elimina duplicados de function_call o function_call_output con el mismo call_id
    /// para evitar que la API de DeepSeek falle con 'Duplicate call_id'.
    /// </summary>
    private static List<JsonNode> SanitizarInput(List<JsonNode> input)
    {
        var sanitizado = new List<JsonNode>();
        var seenCallIds = new HashSet<string>(StringComparer.Ordinal);
        var seenOutputIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in input)
        {
            if (item is JsonObject obj)
            {
                var tipo = obj["type"]?.GetValue<string>();
                var callId = obj["call_id"]?.GetValue<string>();

                if (tipo == "function_call" && !string.IsNullOrEmpty(callId))
                {
                    if (!seenCallIds.Add(callId))
                        continue;
                }
                else if (tipo == "function_call_output" && !string.IsNullOrEmpty(callId))
                {
                    if (!seenOutputIds.Add(callId))
                        continue;
                }
            }

            sanitizado.Add(item);
        }

        return sanitizado;
    }

    private static int ContarUsuarios(List<JsonNode> historial)
    {
        var n = 0;
        foreach (var item in historial)
        {
            if (EsUsuario(item)) n++;
        }
        return n;
    }

    private static bool EsUsuario(JsonNode? item)
        => item is JsonObject obj
            && obj["type"]?.GetValue<string>() == "message"
            && obj["role"]?.GetValue<string>() == "user";

    // ------ Modo espontáneo ------

    /// <summary>Activar/desactivar el modo espontáneo en memoria (caché por servidor).</summary>
    public void EstablecerEspontaneo(ulong guildId, bool habilitado)
        => _espontaneoHabilitado[guildId] = habilitado;

    /// <summary>Consulta rápida (sin BD) del estado del modo espontáneo.</summary>
    public bool EspontaneoHabilitado(ulong guildId)
        => _espontaneoHabilitado.TryGetValue(guildId, out var v) && v;

    /// <summary>
    /// Registra un mensaje ambiental (no dirigido al bot). Incrementa el contador
    /// del servidor y devuelve true si toca soltar un comentario espontáneo.
    /// </summary>
    public bool RegistrarMensajeParaEspontaneo(ulong guildId, string autor, string texto)
    {
        var opts = _options.CurrentValue;
        var estado = _espontaneo.GetOrAdd(guildId, _ => new EstadoEspontaneo(opts));
        lock (estado)
        {
            estado.Recientes.Enqueue((autor, texto));
            while (estado.Recientes.Count > Math.Max(1, opts.SpontaneousRecentBuffer))
                estado.Recientes.Dequeue();
            estado.Contador++;
            if (estado.Contador < estado.Umbral) return false;
            estado.Reiniciar(opts);
            return true;
        }
    }

    /// <summary>Instantánea de los últimos mensajes ambientales (para dar contexto al modelo).</summary>
    public IReadOnlyList<(string Autor, string Texto)> ObtenerRecientes(ulong guildId)
    {
        if (!_espontaneo.TryGetValue(guildId, out var estado))
            return Array.Empty<(string, string)>();
        lock (estado) return estado.Recientes.ToArray();
    }

    /// <summary>
    /// Genera un comentario espontáneo a partir de los últimos mensajes del
    /// canal. No contamina la conversación compartida de /talk: es una llamada
    /// puntual sin historial, SIN búsqueda web y SIN tools de comandos. El
    /// modelo elige por sí mismo el idioma del comentario.
    /// </summary>
    public async Task<string> GenerarComentarioEspontaneoAsync(
        ulong guildId,
        IReadOnlyList<(string Autor, string Texto)> mensajesRecientes)
    {
        var opts = _options.CurrentValue;
        if (mensajesRecientes.Count == 0)
            throw new DeepSeekException("There are no recent messages to comment on.");

        var sb = new StringBuilder();
        sb.AppendLine("This is the server's recent conversation (the users are not talking to you directly, you are reading the chat):");
        sb.AppendLine();
        foreach (var (autor, texto) in mensajesRecientes)
            sb.AppendLine($"{autor}: {texto}");
        sb.AppendLine();
        sb.Append("Make a short, casual and natural comment, as if you were just another server member talking from the chat app. ");
        sb.Append("You can greet someone or pick up on something that was discussed. ");
        sb.Append("Do not mention that you are an AI or that anyone asked you to do this. ");
        sb.Append("Reply only with the message, without quotes or tags.");

        var input = new List<JsonNode> { CrearItemUsuario(sb.ToString()) };
        var (textoFinal, _, _) = await LlamarAsync(
            input, opts, conBusqueda: false, conComandos: false, ct: default).ConfigureAwait(false);
        return textoFinal;
    }

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
            throw new DeepSeekException("Could not reach DeepSeek: " + ex.Message, ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new DeepSeekException("DeepSeek took too long to respond (timeout).", ex);
        }
    }

    private static string ConstruirCuerpo2(
        List<JsonNode> input, DeepSeekOptions opts, bool conBusqueda, List<DeepSeekTool> tools)
    {
        var todas = new List<DeepSeekTool>();
        if (conBusqueda) todas.Add(new DeepSeekTool(WebSearchTool));
        todas.AddRange(tools);

        var req = new DeepSeekRequest(
            Environment.GetEnvironmentVariable("DEEPSEEK_MODEL")?.Trim() is { Length: > 0 } env
                ? env
                : opts.Model,
            Instructions: opts.SystemPrompt,
            Input: SanitizarInput(input),
            Tools: todas.Count > 0 ? todas : null,
            ToolChoice: todas.Count > 0 ? "auto" : null,
            Temperature: opts.Temperature,
            MaxOutputTokens: opts.MaxOutputTokens,
            Stream: false);

        return JsonSerializer.Serialize(req, JsonOpts);
    }

    // ------ estado por servidor ------

    private sealed class Conversacion
    {
        /// <summary>Items de la Responses API (message + tool calls/outputs) por turno.</summary>
        public List<JsonNode> Historial { get; } = new();
        public SemaphoreSlim Puerta { get; } = new(1, 1);

        /// <summary>Si hay una confirmación de comando destructivo pendiente.</summary>
        public bool ConfirmacionPendiente;

        private int _solicitudesReservadas;

        public bool IntentarReservar(int maximo)
        {
            while (true)
            {
                var actuales = Volatile.Read(ref _solicitudesReservadas);
                if (actuales >= Math.Max(1, maximo))
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

    // ------ DTOs serializados como JSON (formato Responses API) ------

    private sealed record DeepSeekTool(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("name")] string? Name = null,
        [property: JsonPropertyName("description")] string? Description = null,
        [property: JsonPropertyName("parameters")] JsonNode? Parameters = null);

    private sealed record DeepSeekRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("instructions")] string Instructions,
        [property: JsonPropertyName("input")] List<JsonNode> Input,
        [property: JsonPropertyName("tools")] List<DeepSeekTool>? Tools,
        [property: JsonPropertyName("tool_choice")] string? ToolChoice,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_output_tokens")] int MaxOutputTokens,
        [property: JsonPropertyName("stream")] bool Stream);

    /// <summary>
    /// Contador espontáneo por servidor. Umbral = Base + jitter(min..max):
    /// tras N mensajes ambientales, espera un extra aleatorio antes de comentar.
    /// </summary>
    private sealed class EstadoEspontaneo
    {
        public int Contador;
        public int Umbral;
        public readonly Queue<(string Autor, string Texto)> Recientes = new();

        public EstadoEspontaneo(DeepSeekOptions opts)
            => Umbral = CalcularUmbral(opts);

        public void Reiniciar(DeepSeekOptions opts)
        {
            Contador = 0;
            Umbral = CalcularUmbral(opts);
        }

        private static int CalcularUmbral(DeepSeekOptions opts)
        {
            var min = Math.Max(0, opts.SpontaneousJitterMin);
            var max = Math.Max(min, opts.SpontaneousJitterMax);
            return Math.Max(1, opts.SpontaneousBaseMessages) + Random.Shared.Next(min, max + 1);
        }
    }
}

/// <summary>
/// Error de comunicación con la API de DeepSeek.
/// </summary>
public class DeepSeekException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Indica que el servidor ya tiene el máximo de solicitudes de chat pendientes.</summary>
public sealed class DeepSeekBusyException(string message) : DeepSeekException(message);
