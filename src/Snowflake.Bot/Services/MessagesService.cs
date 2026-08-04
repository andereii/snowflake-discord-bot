using Microsoft.Extensions.Configuration;

namespace Snowflake.Bot.Services;

/// <summary>
/// Acceso centralizado a los textos del bot, definidos en messages.json.
/// Edita ese archivo para cambiar cualquier mensaje sin tocar el código.
/// </summary>
public sealed class MessagesService(IConfiguration config)
{
    /// <summary>
    /// Obtiene un mensaje por su clave (secciones separadas con ':') y
    /// sustituye los placeholders {nombre} por los valores indicados.
    /// </summary>
    public string Get(string clave, params (string Nombre, object? Valor)[] placeholders)
    {
        var texto = config[clave];
        if (string.IsNullOrEmpty(texto))
            return $"⚠️ Mensaje no encontrado: `{clave}`";

        foreach (var (nombre, valor) in placeholders)
            texto = texto.Replace("{" + nombre + "}", valor?.ToString() ?? string.Empty);

        return texto;
    }
}
