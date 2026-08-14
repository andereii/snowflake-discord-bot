using System.Text.RegularExpressions;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Utilities;

/// <summary>
/// Utilidades para interpretar y mostrar duraciones escritas por usuarios ("10m", "2h", "7d").
/// </summary>
public static partial class DurationParser
{
    [GeneratedRegex(@"^(\d+)\s*([smhd])$", RegexOptions.IgnoreCase)]
    private static partial Regex FormatoRegex();

    // Unidades localizadas (regla i18n: si se añade un idioma, ampliar esta tabla).
    private static readonly IReadOnlyDictionary<string, (string Dia, string Hora, string Minuto, string Segundo)> Unidades =
        new Dictionary<string, (string, string, string, string)>
        {
            [Languages.English] = ("day(s)", "hour(s)", "minute(s)", "second(s)"),
            [Languages.Spanish] = ("día(s)", "hora(s)", "minuto(s)", "segundo(s)"),
            [Languages.Portuguese] = ("dia(s)", "hora(s)", "minuto(s)", "segundo(s)"),
        };

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
    /// Devuelve la duración en formato legible localizado: "3 day(s)",
    /// "2 hour(s)", "15 minute(s)" (según el idioma indicado; inglés por defecto).
    /// </summary>
    public static string Format(TimeSpan d, string locale = Languages.English)
    {
        locale = Languages.Normalizar(locale);
        var u = Unidades[locale];
        return d.TotalDays >= 1 ? $"{d.TotalDays:0.#} {u.Dia}"
            : d.TotalHours >= 1 ? $"{d.TotalHours:0.#} {u.Hora}"
            : d.TotalMinutes >= 1 ? $"{d.TotalMinutes:0.#} {u.Minuto}"
            : $"{d.TotalSeconds:0} {u.Segundo}";
    }
}
