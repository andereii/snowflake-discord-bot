using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Snowflake.Bot.Configuration;
using Snowflake.Bot.Services.Ai;
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

    /// <summary>True si el proveedor realizó alguna búsqueda web durante el turno.</summary>
    public bool UsoBusquedaWeb { get; init; }

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
    ItemLlamadaFuncion Llamada,
    string DescripcionComando);

/// <summary>Hay una confirmación de comando pendiente en el servidor.</summary>
public sealed class AiConfirmationPendingException(string message) : Exception(message);

/// <summary>
/// Chatbot con IA multi-proveedor (DeepSeek o Gemini según las claves de API
/// disponibles). Mantiene una conversación compartida por servidor, puede
/// buscar en internet (a criterio del modelo) y puede ejecutar comandos del
/// bot desde el chat (tools de AiCommandExecutor). El system prompt va
/// SIEMPRE en inglés.
/// </summary>
public sealed class AiService
{
    /// <summary>Iteraciones máximas del bucle tool-call por turno.</summary>
    private const int MaxIteraciones = 5;

    private readonly IOptionsMonitor<AiOptions> _options;
    private readonly GuildSettingsService _settings;
    private readonly AiCommandExecutor _executor;
    private readonly IReadOnlyList<IAiBackend> _backends;
    private readonly ConcurrentDictionary<ulong, Conversacion> _conversaciones = new();

    // Message ID -> guild ID. Solo se guardan mensajes producidos por este chatbot.
    private readonly ConcurrentDictionary<ulong, ulong> _mensajesGenerados = new();

    // Toggle de cháchara espontánea en memoria (para no tocar la BD en cada mensaje).
    private readonly ConcurrentDictionary<ulong, bool> _espontaneoHabilitado = new();

    // Estado del contador espontáneo por servidor.
    private readonly ConcurrentDictionary<ulong, EstadoEspontaneo> _espontaneo = new();

    public AiService(
        IEnumerable<IAiBackend> backends,
        IOptionsMonitor<AiOptions> options,
        GuildSettingsService settings,
        AiCommandExecutor executor)
    {
        _backends = backends.ToList();
        _options = options;
        _settings = settings;
        _executor = executor;
    }

    /// <summary>Nombre del backend activo (para diagnóstico del panel).</summary>
    public string ProveedorActivo
    {
        get
        {
            try { return SeleccionarBackend().Nombre; }
            catch (AiException) { return "none"; }
        }
    }

    /// <summary>
    /// Selecciona el backend según la variable AI_PROVIDER (opcional) o, en
    /// automático, el primero con clave de API configurada. Si no hay ninguna
    /// clave, lanza <see cref="AiApiKeyMissingException"/>.
    /// </summary>
    private IAiBackend SeleccionarBackend()
    {
        var pref = Environment.GetEnvironmentVariable("AI_PROVIDER")?.Trim().ToLowerInvariant();

        if (pref is "gemini" or "deepseek")
        {
            var forzado = _backends.FirstOrDefault(b =>
                b.Nombre.Equals(pref, StringComparison.OrdinalIgnoreCase));
            if (forzado is null || !forzado.Disponible)
                throw new AiApiKeyMissingException($"AI_PROVIDER={pref} but its API key is missing.");
            return forzado;
        }

        return _backends.FirstOrDefault(b => b.Disponible)
            ?? throw new AiApiKeyMissingException("No AI API key is configured.");
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
            throw new AiException("The message cannot be empty.");

        if (texto.Length > opts.MaxInputLength)
            throw new AiException($"The message exceeds the maximum of {opts.MaxInputLength} characters.");

        // Lanza AiApiKeyMissingException temprano si no hay ninguna clave.
        SeleccionarBackend();

        var cfg = await _settings.GetAsync(ctx.Guild.Id).ConfigureAwait(false);
        var conBusqueda = cfg.AiWebSearchEnabled;
        var conComandos = cfg.AiCommandsEnabled;

        var conversacion = _conversaciones.GetOrAdd(ctx.Guild.Id, _ => new Conversacion());
        if (!conversacion.IntentarReservar(opts.MaxConcurrentPerGuild))
            throw new AiBusyException(
                "Too many chat requests are already pending on this server.");

        var itemUsuario = new ItemMensaje("user", $"[{autor}] {texto}");
        try
        {
            await conversacion.Puerta.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (conversacion.ConfirmacionPendiente)
                    throw new AiConfirmationPendingException(
                        "There is a pending command confirmation on this server.");

                RecortarHistorial(conversacion.Historial, opts.MaxHistoryTurns);
                conversacion.Historial.Add(itemUsuario);

                try
                {
                    var resultado = await BucleAsync(
                        conversacion, ctx, opts, conBusqueda, conComandos, ct).ConfigureAwait(false);
                    RecortarHistorial(conversacion.Historial, opts.MaxHistoryTurns);
                    return resultado;
                }
                catch
                {
                    // No conservamos un mensaje que el modelo no llegó a contestar.
                    conversacion.Historial.Remove(itemUsuario);
                    throw;
                }
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

            var yaTieneCall = conversacion.Historial.Any(h =>
                h is ItemLlamadaFuncion f && f.CallId == pendiente.CallId);
            if (!yaTieneCall)
                conversacion.Historial.Add(pendiente.Llamada);

            var yaTieneOutput = conversacion.Historial.Any(h =>
                h is ItemResultadoFuncion r && r.CallId == pendiente.CallId);
            if (!yaTieneOutput)
                conversacion.Historial.Add(
                    new ItemResultadoFuncion(pendiente.CallId, pendiente.ToolName, resultadoTexto));

            var cfg = await _settings.GetAsync(ctx.Guild.Id).ConfigureAwait(false);
            var resultado = await BucleAsync(
                conversacion, ctx, opts, cfg.AiWebSearchEnabled, cfg.AiCommandsEnabled, ct)
                .ConfigureAwait(false);

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
    /// Bucle principal de un turno: llama a la IA y, si el modelo pide tools,
    /// las ejecuta (salvo las destructivas, que interrumpen con pendiente) y
    /// continúa hasta obtener texto final o agotar iteraciones.
    /// </summary>
    private async Task<AiChatOutcome> BucleAsync(
        Conversacion conversacion,
        AiCommandContext ctx,
        AiOptions opts,
        bool conBusqueda,
        bool conComandos,
        CancellationToken ct)
    {
        var backend = SeleccionarBackend();
        var ejecutados = new List<AiCommandResult>();
        var usoBusqueda = false;

        for (var iteracion = 0; iteracion < MaxIteraciones; iteracion++)
        {
            var tools = conComandos ? _executor.Herramientas.ToList() : [];
            var resp = await backend.LlamarAsync(
                conversacion.Historial, tools, conBusqueda, ct).ConfigureAwait(false);

            conversacion.Historial.AddRange(resp.ItemsSalida);
            if (resp.UsoBusquedaWeb) usoBusqueda = true;

            if (resp.Llamadas.Count == 0)
                return new AiChatOutcome
                {
                    Texto = resp.Texto,
                    Comandos = ejecutados,
                    UsoBusquedaWeb = usoBusqueda
                };

            foreach (var llamada in resp.Llamadas)
            {
                var args = ParsearArgs(llamada.ArgsJson);

                var ejecucion = await _executor.EjecutarAsync(ctx, llamada.Nombre, args).ConfigureAwait(false);

                if (ejecucion.Destructivo)
                {
                    // Comando destructivo: se detiene el turno y se pide
                    // confirmación con botones al usuario que lo solicitó.
                    conversacion.ConfirmacionPendiente = true;
                    var pendiente = new PendingCommand(
                        Token: Guid.NewGuid().ToString("N"),
                        ToolName: llamada.Nombre,
                        Args: args,
                        CallId: llamada.CallId,
                        Llamada: new ItemLlamadaFuncion(llamada.CallId, llamada.Nombre, llamada.ArgsJson),
                        DescripcionComando: ejecucion.DescripcionComando);
                    return new AiChatOutcome
                    {
                        Pendiente = pendiente,
                        Comandos = ejecutados,
                        UsoBusquedaWeb = usoBusqueda
                    };
                }

                if (ejecucion.Resultado is { } resultado)
                {
                    ejecutados.Add(resultado);
                    conversacion.Historial.Add(
                        new ItemResultadoFuncion(llamada.CallId, llamada.Nombre, resultado.Texto));
                }
            }
        }

        // Se agotaron las iteraciones: devolvemos el último texto disponible.
        return new AiChatOutcome { Texto = "…", Comandos = ejecutados, UsoBusquedaWeb = usoBusqueda };
    }

    private static JsonObject ParsearArgs(string argsJson)
    {
        try
        {
            return string.IsNullOrWhiteSpace(argsJson)
                ? new JsonObject()
                : JsonNode.Parse(argsJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    /// <summary>
    /// Mantiene el historial acotado a <paramref name="turnos"/> mensajes de
    /// usuario: se eliminan turnos completos por el principio, sin dejar items
    /// huérfanos (llamadas/resultados) al inicio.
    /// </summary>
    private static void RecortarHistorial(List<ItemHistorial> historial, int turnos)
    {
        var max = Math.Max(1, turnos);

        while (ContarUsuarios(historial) > max)
            historial.RemoveAt(0);

        while (historial.Count > 0 && historial[0] is not ItemMensaje { Rol: "user" })
            historial.RemoveAt(0);
    }

    private static int ContarUsuarios(List<ItemHistorial> historial)
        => historial.Count(h => h is ItemMensaje { Rol: "user" });

    // ------ modo espontáneo ------

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
        if (mensajesRecientes.Count == 0)
            throw new AiException("There are no recent messages to comment on.");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("This is the server's recent conversation (the users are not talking to you directly, you are reading the chat):");
        sb.AppendLine();
        foreach (var (autor, texto) in mensajesRecientes)
            sb.AppendLine($"{autor}: {texto}");
        sb.AppendLine();
        sb.Append("Make a short, casual and natural comment, as if you were just another server member talking from the chat app. ");
        sb.Append("You can greet someone or pick up on something that was discussed. ");
        sb.Append("Do not mention that you are an AI or that anyone asked you to do this. ");
        sb.Append("Reply only with the message, without quotes or tags.");

        var backend = SeleccionarBackend();
        var historial = new List<ItemHistorial> { new ItemMensaje("user", sb.ToString()) };
        var resp = await backend.LlamarAsync(historial, [], conBusqueda: false, default).ConfigureAwait(false);
        return resp.Texto;
    }

    // ------ estado por servidor ------

    private sealed class Conversacion
    {
        /// <summary>Items normalizados de la conversación por turno.</summary>
        public List<ItemHistorial> Historial { get; } = new();
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

    /// <summary>
    /// Contador espontáneo por servidor. Umbral = Base + jitter(min..max):
    /// tras N mensajes ambientales, espera un extra aleatorio antes de comentar.
    /// </summary>
    private sealed class EstadoEspontaneo
    {
        public int Contador;
        public int Umbral;
        public readonly Queue<(string Autor, string Texto)> Recientes = new();

        public EstadoEspontaneo(AiOptions opts)
            => Umbral = CalcularUmbral(opts);

        public void Reiniciar(AiOptions opts)
        {
            Contador = 0;
            Umbral = CalcularUmbral(opts);
        }

        private static int CalcularUmbral(AiOptions opts)
        {
            var min = Math.Max(0, opts.SpontaneousJitterMin);
            var max = Math.Max(min, opts.SpontaneousJitterMax);
            return Math.Max(1, opts.SpontaneousBaseMessages) + Random.Shared.Next(min, max + 1);
        }
    }
}

/// <summary>Error de comunicación con el proveedor de IA.</summary>
public class AiException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Indica que el servidor ya tiene el máximo de solicitudes de chat pendientes.</summary>
public sealed class AiBusyException(string message) : AiException(message);

/// <summary>Indica que no hay ninguna clave de API de IA configurada.</summary>
public sealed class AiApiKeyMissingException(string message) : AiException(message);
