namespace Snowflake.Bot.Data.Entities;

/// <summary>
/// Configuración general del bot por servidor.
/// Todos los ajustes que aquí viven son editables desde Discord (comandos) y
/// están pensados para exponerse también en el panel web de configuración
/// (ver GuildSettingsService, que los cachea y sirve de punto único de acceso).
/// </summary>
public sealed class GuildConfig
{
    /// <summary>Id del servidor (clave primaria).</summary>
    public ulong GuildId { get; set; }

    // ------------------------- Moderación -------------------------

    /// <summary>Canal donde se anuncian los incidentes de moderación.</summary>
    public ulong? ModLogChannelId { get; set; }

    // ------------------------- Bienvenida -------------------------

    /// <summary>Canal donde se envían los mensajes de bienvenida.</summary>
    public ulong? WelcomeChannelId { get; set; }

    /// <summary>Plantilla del mensaje de bienvenida ({usuario}, {servidor}).</summary>
    public string? WelcomeMessage { get; set; }

    // ------------------------- Canales de voz -------------------------

    /// <summary>Canal "hub" del sistema join-to-create de canales de voz.</summary>
    public ulong? HubChannelId { get; set; }

    /// <summary>
    /// Plantilla del nombre de los canales temporales (placeholder {usuario}).
    /// Null = usar el texto por defecto de messages.json ("Voces:NombreTemporal").
    /// </summary>
    public string? TempChannelNameTemplate { get; set; }

    // ------------------------- Música -------------------------

    /// <summary>
    /// Volumen de música persistente para el servidor (0-100).
    /// Null = usar el volumen por defecto de Lavalink (100).
    /// </summary>
    public int? Volume { get; set; }

    /// <summary>
    /// Rol de DJ: si está establecido, solo quienes lo tengan (o ManageGuild)
    /// pueden saltar, pausar o detener la música. Null = cualquiera que esté
    /// en el mismo canal de voz que el bot.
    /// </summary>
    public ulong? DjRoleId { get; set; }

    // ------------------------- IA -------------------------

    /// <summary>Si /charlar está habilitado en el servidor (interruptor general).</summary>
    public bool AiChatEnabled { get; set; } = true;

    /// <summary>
    /// Si el bot debe responder con la IA cuando lo mencionan con @.
    /// Desactivado por defecto; se activa con /ai-mentions.
    /// </summary>
    public bool AiMentionsEnabled { get; set; }

    /// <summary>
    /// Si el bot puede intervenir espontáneamente en el chat ambiental,
    /// sin que nadie lo mencione. Desactivado por defecto.
    /// </summary>
    public bool AiSpontaneousEnabled { get; set; }

    /// <summary>
    /// Si la IA puede buscar en internet cuando el
    /// modelo lo considere necesario. Activado por defecto; los administradores
    /// pueden desactivarlo con /ai-search o desde el panel web.
    /// </summary>
    public bool AiWebSearchEnabled { get; set; } = true;

    /// <summary>
    /// Si la IA puede interpretar instrucciones del chat como comandos del bot
    /// y ejecutarlos (con los mismos permisos que los slash commands; los
    /// destructivos piden confirmación con botones). Activado por defecto;
    /// se desactiva con /ai-commands o desde el panel web.
    /// </summary>
    public bool AiCommandsEnabled { get; set; } = true;

    // ------------------------- Descargas -------------------------

    /// <summary>Si /download está habilitado en el servidor.</summary>
    public bool DownloadsEnabled { get; set; } = true;

    // ------------------------- Encuestas -------------------------

    /// <summary>Contador de encuestas creadas en este servidor (persistente).</summary>
    public int PollCount { get; set; }

    // ------------------------- Idioma -------------------------

    /// <summary>
    /// Idioma de los mensajes del bot en este servidor: "en", "es" o "pt".
    /// Por defecto inglés. Se cambia con /lang o desde el panel web.
    /// </summary>
    public string Language { get; set; } = "en";
}
