namespace Snowflake.Bot.Services.Settings;

/// <summary>
/// Contrato serializable (JSON) con TODOS los ajustes de un servidor, pensado
/// para el panel web de configuración. Los IDs de Discord se exponen como
/// string porque JavaScript pierde precisión con enteros > 2^53.
/// Cada sección agrupa los ajustes de un módulo, como en los dashboards de
/// los bots comerciales (MEE6, Dyno, Carl-bot…).
/// </summary>
public sealed record GuildSettingsSnapshot
{
    public required string GuildId { get; init; }

    public ModerationSection Moderation { get; init; } = new();
    public WelcomeSection Welcome { get; init; } = new();
    public VoiceSection Voice { get; init; } = new();
    public MusicSection Music { get; init; } = new();
    public AiSection Ai { get; init; } = new();
    public DownloadsSection Downloads { get; init; } = new();

    /// <summary>IDs (string) de los canales en lockdown (/bloquear).</summary>
    public List<string> BlockedChannels { get; init; } = [];

    /// <summary>Ajustes (y estado actual) del juego de conteo; null si nunca se configuró.</summary>
    public CountingSection? Counting { get; init; }

    /// <summary>Suscripción de YouTube; null si el servidor no tiene ninguna.</summary>
    public YouTubeSection? YouTube { get; init; }

    // ------------------------- Secciones -------------------------

    public sealed record ModerationSection
    {
        /// <summary>Canal de logs de moderación (null = sin anuncios).</summary>
        public string? LogChannelId { get; init; }
    }

    public sealed record WelcomeSection
    {
        /// <summary>Si la bienvenida está activa (hay canal configurado).</summary>
        public bool Enabled { get; init; }
        public string? ChannelId { get; init; }

        /// <summary>Plantilla con {usuario} y {servidor}; null = mensaje por defecto del bot.</summary>
        public string? Message { get; init; }
    }

    public sealed record VoiceSection
    {
        /// <summary>Canal hub del join-to-create (null = desactivado).</summary>
        public string? HubChannelId { get; init; }

        /// <summary>Plantilla del nombre de canales temporales ({usuario}); null = por defecto.</summary>
        public string? TempChannelNameTemplate { get; init; }
    }

    public sealed record MusicSection
    {
        /// <summary>Volumen persistente 0-100 (null = volumen por defecto del reproductor).</summary>
        public int? Volume { get; init; }

        /// <summary>Rol DJ (null = cualquiera en el mismo canal de voz puede controlar).</summary>
        public string? DjRoleId { get; init; }
    }

    public sealed record AiSection
    {
        /// <summary>Interruptor general de /charlar.</summary>
        public bool ChatEnabled { get; init; }

        /// <summary>Responder cuando mencionan al bot con @.</summary>
        public bool MentionsEnabled { get; init; }

        /// <summary>Comentarios espontáneos en el chat ambiental.</summary>
        public bool SpontaneousEnabled { get; init; }
    }

    public sealed record DownloadsSection
    {
        /// <summary>Interruptor general de /descargar.</summary>
        public bool Enabled { get; init; }
    }

    public sealed record CountingSection
    {
        public bool Enabled { get; init; }
        public string? ChannelId { get; init; }
        public string Base { get; init; } = "Decimal";
        public long? Goal { get; init; }
        public int ExtraChancesPerDay { get; init; }
        public string? EmojiCorrect { get; init; }
        public string? EmojiIncorrect { get; init; }
        public string? EmojiRecord { get; init; }
        public string? LoseMessage { get; init; }

        // Estado actual del juego (solo lectura para el panel).
        public long CurrentValue { get; init; }
        public long CurrentRecord { get; init; }
    }

    public sealed record YouTubeSection
    {
        public required string ChannelId { get; init; }
        public required string ChannelName { get; init; }
        public required string NotifyChannelId { get; init; }
        public string? NotifyRoleId { get; init; }
        public string? CustomMessage { get; init; }
    }
}
