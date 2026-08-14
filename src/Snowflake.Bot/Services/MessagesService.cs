using Microsoft.Extensions.Configuration;
using Snowflake.Bot.Services.Settings;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Services;

/// <summary>
/// Acceso centralizado a los textos del bot, localizados.
/// Hay un archivo por idioma: messages.en.json, messages.es.json y
/// messages.pt.json (recarga en caliente). El idioma por defecto es el inglés;
/// si una clave no existe en el idioma pedido, se cae al inglés.
///
/// REGLA DEL PROYECTO: todo mensaje nuevo debe existir en los TRES archivos.
/// </summary>
public sealed class MessagesService(IConfiguration config, GuildSettingsService settings)
{
    /// <summary>
    /// Obtiene un mensaje para un SERVIDOR (resuelve su idioma desde la caché
    /// de ajustes; inglés por defecto) y sustituye los placeholders {nombre}.
    /// </summary>
    public string Get(ulong guildId, string clave, params (string Nombre, object? Valor)[] placeholders)
        => GetLocalizado(settings.Locale(guildId), clave, placeholders);

    /// <summary>
    /// Obtiene un mensaje para un IDIOMA explícito ("en"/"es"/"pt"), con
    /// fallback al inglés si la clave falta en ese idioma.
    /// </summary>
    public string Get(string locale, string clave, params (string Nombre, object? Valor)[] placeholders)
        => GetLocalizado(Languages.Normalizar(locale), clave, placeholders);

    /// <summary>Versión en inglés (para contextos sin servidor: DMs, arranque…).</summary>
    public string En(string clave, params (string Nombre, object? Valor)[] placeholders)
        => GetLocalizado(Languages.English, clave, placeholders);

    /// <summary>Idioma activo de un servidor ("en"/"es"/"pt") sin tocar la BD.</summary>
    public string Locale(ulong guildId) => settings.Locale(guildId);

    private string GetLocalizado(
        string locale, string clave, (string Nombre, object? Valor)[] placeholders)
    {
        // Fallback en cadena: idioma pedido -> inglés -> aviso de clave ausente.
        var texto = locale == Languages.English
            ? config[$"{Languages.English}:{clave}"]
            : config[$"{locale}:{clave}"] ?? config[$"{Languages.English}:{clave}"];

        if (string.IsNullOrEmpty(texto))
            return $"⚠️ Message not found: `{clave}`";

        foreach (var (nombre, valor) in placeholders)
            texto = texto.Replace("{" + nombre + "}", valor?.ToString() ?? string.Empty);

        return texto;
    }
}
