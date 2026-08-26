namespace Snowflake.Bot.Data.Entities;

/// <summary>
/// Registra el estado de ausencia (AFK) de un usuario en un servidor.
/// </summary>
public sealed class AfkUser
{
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }

    /// <summary>Motivo por el que el usuario se ausentó.</summary>
    public string Reason { get; set; } = "AFK";

    /// <summary>Momento en que se estableció el estado AFK.</summary>
    public DateTimeOffset SetAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Apodo del usuario antes de ponerse AFK (para restaurarlo).</summary>
    public string? OriginalNickname { get; set; }
}
