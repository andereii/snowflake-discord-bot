namespace Snowflake.Bot.Data.Entities;

/// <summary>
/// Canal de voz temporal creado por el sistema join-to-create.
/// Se elimina cuando queda vacío.
/// </summary>
public sealed class TempChannel
{
    /// <summary>Id del canal de voz (clave primaria).</summary>
    public ulong ChannelId { get; set; }
    public ulong GuildId { get; set; }

    /// <summary>Usuario que lo provocó (tiene permisos sobre él).</summary>
    public ulong OwnerUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}