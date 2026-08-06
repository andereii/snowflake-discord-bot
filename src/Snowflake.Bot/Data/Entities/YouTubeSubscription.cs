namespace Snowflake.Bot.Data.Entities;

/// <summary>
/// Suscripción de un servidor a un canal de YouTube: el bot vigila el feed RSS
/// público del canal y avisa en un canal de Discord cuando se sube un vídeo nuevo.
/// Un servidor solo puede tener una suscripción (PK = GuildId).
/// </summary>
public sealed class YouTubeSubscription
{
    /// <summary>Id del servidor de Discord (clave primaria).</summary>
    public ulong GuildId { get; set; }

    /// <summary>Id del canal de YouTube (formato UCxxxx, usado para el feed RSS).</summary>
    public string YTChannelId { get; set; } = string.Empty;

    /// <summary>Nombre legible del canal de YouTube.</summary>
    public string YTChannelName { get; set; } = string.Empty;

    /// <summary>Canal de Discord donde se enviará la notificación.</summary>
    public ulong NotifyChannelId { get; set; }

    /// <summary>Rol a mencionar en la notificación (opcional). Null = sin mención.</summary>
    public ulong? NotifyRoleId { get; set; }

    /// <summary>
    /// Id del último vídeo visto del feed. Sirve de marca de agua: al suscribirse
    /// se rellena con el vídeo más reciente del feed para NO notificar vídeos
    /// antiguos, solo los nuevos a partir de entonces.
    /// </summary>
    public string? LastVideoId { get; set; }

    /// <summary>
    /// Plantilla personalizada del mensaje de notificación. Se envía ANTES del
    /// enlace del vídeo. Placeholders (se sustituyen al notificar):
    /// <list type="bullet">
    /// <item>{canal}   — nombre del canal de YouTube</item>
    /// <item>{titulo}  — título del vídeo</item>
    /// <item>{autor}   — nombre del autor (igual que {canal} en YT)</item>
    /// <item>{url}     — enlace del vídeo</item>
    /// <item>{videoId} — id del vídeo</item>
    /// <item>{subido}  — fecha ISO 8601 de publicación (p. ej. 2026-08-05T12:00:00+00:00)</item>
    /// <item>{subidoREL} — subido hace X (formato relativo, p. ej. "hace 2 minutos")</item>
    /// </list>
    /// Null = usar el texto por defecto de messages.json.
    /// </summary>
    public string? CustomMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}