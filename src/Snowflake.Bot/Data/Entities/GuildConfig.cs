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

    // ------------------------- IA (Gemini) -------------------------

    /// <summary>Si /charlar está habilitado en el servidor (interruptor general).</summary>
    public bool GeminiChatEnabled { get; set; } = true;

    /// <summary>
    /// Si el bot debe responder con Gemini cuando lo mencionan con @.
    /// Desactivado por defecto; se activa con /gemini-menciones.
    /// </summary>
    public bool GeminiMentionsEnabled { get; set; }

    /// <summary>
    /// Si el bot puede intervenir espontáneamente en el chat ambiental,
    /// sin que nadie lo mencione. Desactivado por defecto.
    /// </summary>
    public bool GeminiSpontaneousEnabled { get; set; }

    // ------------------------- Descargas -------------------------

    /// <summary>Si /descargar está habilitado en el servidor.</summary>
    public bool DownloadsEnabled { get; set; } = true;
}
