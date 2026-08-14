namespace Snowflake.Bot.Utilities;

/// <summary>
/// Idiomas soportados por el bot. Cada mensaje de messages.*.json y cada
/// comando slash (name/description localizations) debe existir en los TRES.
/// Regla del proyecto: al añadir un mensaje o comando nuevo, crear siempre
/// las 3 versiones (en/es/pt).
/// </summary>
public static class Languages
{
    public const string English = "en";
    public const string Spanish = "es";
    public const string Portuguese = "pt";

    /// <summary>Idiomas soportados, en orden de preferencia de fallback.</summary>
    public static readonly string[] Supported = [English, Spanish, Portuguese];

    /// <summary>Devuelve el idioma si está soportado; si no, inglés (por defecto).</summary>
    public static string Normalizar(string? idioma)
        => idioma is not null && Supported.Contains(idioma) ? idioma : English;

    /// <summary>Nombre legible del idioma (para prompts y menús).</summary>
    public static string Nombre(string locale) => Normalizar(locale) switch
    {
        Spanish => "Spanish",
        Portuguese => "Portuguese",
        _ => "English"
    };
}
