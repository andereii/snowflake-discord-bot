namespace Snowflake.Bot.Data.Entities;

/// <summary>Tipo de acción de moderación registrada.</summary>
public enum IncidentType
{
    Advertencia,
    Expulsion,
    Veto,
    Aislamiento,
    FinAislamiento
}

/// <summary>
/// Un incidente de moderación documentado, para el historial del servidor.
/// </summary>
public sealed class Incident
{
    /// <summary>Número de caso (autoincremental).</summary>
    public int Id { get; set; }

    public ulong GuildId { get; set; }

    public ulong TargetUserId { get; set; }
    public string TargetTag { get; set; } = string.Empty;

    public ulong ModeratorId { get; set; }
    public string ModeratorTag { get; set; } = string.Empty;

    public IncidentType Type { get; set; }
    public string Reason { get; set; } = string.Empty;

    /// <summary>Solo para aislamientos: cuánto duró.</summary>
    public TimeSpan? Duration { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
