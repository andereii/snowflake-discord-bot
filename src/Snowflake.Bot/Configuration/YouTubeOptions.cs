namespace Snowflake.Bot.Configuration;

/// <summary>
/// Opciones del vigilante de YouTube (feed RSS). Sección "YouTube" de appsettings.json.
/// </summary>
public sealed class YouTubeOptions
{
    /// <summary>Minutos entre revisión y revisión de los feeds RSS suscritos.</summary>
    public int PollIntervalMinutes { get; set; } = 5;

    /// <summary>Espera inicial (segundos) tras arrancar, para dar tiempo a conectar con Discord.</summary>
    public int StartupDelaySeconds { get; set; } = 15;

    /// <summary>Timeout (segundos) de yt-dlp al resolver una URL/@handle a channel_id.</summary>
    public int ResolveTimeoutSeconds { get; set; } = 20;

    /// <summary>Timeout (segundos) de yt-dlp al pedir el nombre legible de un canal.</summary>
    public int NameResolveTimeoutSeconds { get; set; } = 15;
}
