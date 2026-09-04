namespace Snowflake.Bot.Data.Entities;

/// <summary>
/// Cumpleaños registrado por un usuario en un servidor. La clave compuesta
/// (GuildId, UserId) garantiza una sola entrada por usuario y servidor.
/// El año es opcional: si el usuario no quiere compartir su edad.
/// </summary>
public sealed class Birthday
{
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    public int Day { get; set; }
    public int Month { get; set; }
    public int? Year { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Configuración de la celebración de cumpleaños por servidor.
/// Solo editable desde el panel web (no hay comando slash para esto).
/// </summary>
public sealed class BirthdayConfig
{
    public ulong GuildId { get; set; }

    /// <summary>Si la felicitación automática está habilitada.</summary>
    public bool Enabled { get; set; }

    /// <summary>Canal donde se anuncian los cumpleaños. Null = deshabilitado.</summary>
    public ulong? ChannelId { get; set; }

    /// <summary>Hora local (UTC) del día en que se publica la felicitación (0-23).</summary>
    public int HourUtc { get; set; } = 12;

    /// <summary>Mensaje de felicitación. Placeholders: {usuario}, {servidor}, {edad}.</summary>
    public string Message { get; set; } = "¡Feliz cumpleaños {usuario}! 🎂🎉";
}
