namespace Snowflake.Bot.Configuration;

/// <summary>
/// Conexión al servidor Lavalink (para la música). Sección "Lavalink" de appsettings.json.
/// </summary>
public sealed class LavalinkOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 2333;
    public string Password { get; set; } = "youshallnotpass";
}