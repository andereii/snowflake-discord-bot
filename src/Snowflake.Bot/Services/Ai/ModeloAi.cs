using System.Text.Json.Nodes;
using Snowflake.Bot.Services.AiCommands;

namespace Snowflake.Bot.Services.Ai;

/// <summary>Item normalizado del historial de conversación (agnóstico del proveedor).</summary>
public abstract record ItemHistorial;

/// <summary>Mensaje de texto de un rol ("user" o "assistant").</summary>
public sealed record ItemMensaje(string Rol, string Texto) : ItemHistorial;

/// <summary>Petición de llamada a función emitida por el modelo.</summary>
public sealed record ItemLlamadaFuncion(string CallId, string Nombre, string ArgsJson) : ItemHistorial;

/// <summary>Resultado devuelto a una llamada de función.</summary>
public sealed record ItemResultadoFuncion(string CallId, string Nombre, string Salida) : ItemHistorial;

/// <summary>Marcador de que el proveedor realizó una búsqueda web en ese punto.</summary>
public sealed record ItemBusquedaWeb : ItemHistorial;

/// <summary>Llamada a función pedida por el modelo en una respuesta.</summary>
public sealed record LlamadaFuncion(string CallId, string Nombre, string ArgsJson);

/// <summary>Respuesta normalizada de un backend de IA en un turno.</summary>
public sealed record RespuestaBackend(
    string Texto,
    IReadOnlyList<LlamadaFuncion> Llamadas,
    IReadOnlyList<ItemHistorial> ItemsSalida,
    bool UsoBusquedaWeb);

/// <summary>
/// Backend de IA (DeepSeek, Gemini…). Convierte el historial normalizado al
/// formato de su API y devuelve la respuesta también normalizada.
/// </summary>
public interface IAiBackend
{
    /// <summary>Nombre técnico del proveedor (logs/diagnóstico).</summary>
    string Nombre { get; }

    /// <summary>True si su clave de API está configurada en el entorno.</summary>
    bool Disponible { get; }

    /// <summary>
    /// Realiza una llamada al modelo con el historial dado, las tools de
    /// comandos del bot y (opcionalmente) búsqueda web a criterio del modelo.
    /// </summary>
    Task<RespuestaBackend> LlamarAsync(
        IReadOnlyList<ItemHistorial> historial,
        IReadOnlyList<ToolDef> tools,
        bool conBusqueda,
        CancellationToken ct);
}
