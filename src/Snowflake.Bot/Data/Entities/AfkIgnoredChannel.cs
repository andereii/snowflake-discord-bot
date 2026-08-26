namespace Snowflake.Bot.Data.Entities;

/// <summary>
/// Canal de texto donde el bot NO remueve el estado AFK de los usuarios al hablar.
/// </summary>
public sealed class AfkIgnoredChannel
{
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
}
