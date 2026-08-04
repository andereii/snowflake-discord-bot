using System.Text.RegularExpressions;

namespace Snowflake.Bot.Utilities;

/// <summary>
/// Utilidades para interpretar y mostrar duraciones escritas por usuarios ("10m", "2h", "7d").
/// </summary>
public static partial class DurationParser
{
    [GeneratedRegex(@"^(\d+)\s*([smhd])$", RegexOptions.IgnoreCase)]
    private static partial Regex FormatoRegex();

    /// <summary>
    /// Intenta interpretar textos como "30s", "10m", "2h" o "7d".
    /// </summary>
    public static bool TryParse(string? texto, out TimeSpan duracion)
    {
        duracion = default;
        if (string.IsNullOrWhiteSpace(texto)) return false;

        var m = FormatoRegex().Match(texto.Trim());
        if (!m.Success) return false;

        var cantidad = long.Parse(m.Groups[1].Value);
        duracion = m.Groups[2].Value.ToLowerInvariant() switch
        {
            "s" => TimeSpan.FromSeconds(cantidad),
            "m" => TimeSpan.FromMinutes(cantidad),
            "h" => TimeSpan.FromHours(cantidad),
            "d" => TimeSpan.FromDays(cantidad),
            _ => default
        };
        return true;
    }

    /// <summary>
    /// Devuelve la duración en formato legible: "3 día(s)", "2 hora(s)", "15 minuto(s)".
    /// </summary>
    public static string Format(TimeSpan d) =>
        d.TotalDays >= 1 ? $"{d.TotalDays:0.#} día(s)"
        : d.TotalHours >= 1 ? $"{d.TotalHours:0.#} hora(s)"
        : d.TotalMinutes >= 1 ? $"{d.TotalMinutes:0.#} minuto(s)"
        : $"{d.TotalSeconds:0} segundo(s)";
}
