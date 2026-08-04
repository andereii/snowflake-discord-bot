namespace Snowflake.Bot.Data.Entities;

/// <summary>
/// Un rol de color de la paleta del servidor, instalado por el bot.
/// </summary>
public sealed class ColorRole
{
    public int Id { get; set; }
    public ulong GuildId { get; set; }
    public ulong RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
}