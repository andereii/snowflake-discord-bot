namespace Snowflake.Bot.Data.Entities;

/// <summary>
/// Configuración del bot por servidor (canal de logs, bienvenida, hub de voz, etc.).
/// </summary>
public sealed class GuildConfig
{
    /// <summary>Id del servidor (clave primaria).</summary>
    public ulong GuildId { get; set; }

    /// <summary>Canal donde se anuncian los incidentes de moderación.</summary>
    public ulong? ModLogChannelId { get; set; }

    /// <summary>Canal donde se envían los mensajes de bienvenida.</summary>
    public ulong? WelcomeChannelId { get; set; }

    /// <summary>Plantilla del mensaje de bienvenida ({usuario}, {servidor}).</summary>
    public string? WelcomeMessage { get; set; }

    /// <summary>Canal "hub" del sistema join-to-create de canales de voz.</summary>
    public ulong? HubChannelId { get; set; }

    /// <summary>
    /// Volumen de música persistente para el servidor (0-100).
    /// Null = usar el volumen por defecto de Lavalink (100).
    /// </summary>
    public int? Volume { get; set; }
}
