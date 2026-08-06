namespace Snowflake.Bot.Configuration;

/// <summary>
/// Opciones del módulo de descargas (yt-dlp). Sección "Downloads" de appsettings.json.
/// </summary>
public sealed class DownloadOptions
{
    /// <summary>
    /// Tamaño máximo (bytes) para adjuntar el archivo directamente en Discord.
    /// Por encima se sube a litterbox y se responde con un enlace.
    /// Por defecto 9 MiB, holgado bajo el límite de subida de Discord (~10 MB sin boost).
    /// </summary>
    public long MaxDiscordBytes { get; set; } = 9_437_184;

    /// <summary>
    /// Minutos máximos que una descarga puede tardar antes de cancelarse
    /// (el temporizador del comando; el servicio añade 1 minuto de margen duro).
    /// </summary>
    public int TimeoutMinutes { get; set; } = 4;
}
