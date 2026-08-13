namespace Snowflake.Bot.Data.Entities;

/// <summary>
/// Estado de un canal bloqueado con /bloquear (lockdown). Guarda el overwrite
/// original del rol @everyone para poder restaurarlo EXACTAMENTE al desbloquear
/// (así se respetan los permisos que ya tenía el canal o su categoría).
/// </summary>
public sealed class ChannelLock
{
    /// <summary>Id del canal bloqueado (clave primaria).</summary>
    public ulong ChannelId { get; set; }

    public ulong GuildId { get; set; }

    /// <summary>Permisos que @everyone tenía permitidos ANTES del bloqueo.</summary>
    public long AllowBits { get; set; }

    /// <summary>Permisos que @everyone tenía denegados ANTES del bloqueo.</summary>
    public long DenyBits { get; set; }

    /// <summary>Si el canal tenía un overwrite explícito para @everyone antes del bloqueo.</summary>
    public bool HadOverwrite { get; set; }

    public DateTimeOffset LockedAt { get; set; } = DateTimeOffset.UtcNow;
}
