namespace Snowflake.Bot.Configuration;

/// <summary>
/// Opciones de colores para los roles autoasignables.
/// </summary>
public sealed class ColorOptions
{
    public Dictionary<string, string> Normal { get; set; } = new();
    public Dictionary<string, string> Pastel { get; set; } = new();
}
