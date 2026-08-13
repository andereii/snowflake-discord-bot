using Microsoft.AspNetCore.Http;

namespace Snowflake.Bot.Endpoints;

/// <summary>
/// Guarda de API key compartida por todos los endpoints del panel web.
/// Si la variable de entorno WEB_PANEL_API_KEY no está definida, las llamadas
/// pasan sin clave (desarrollo). Si está definida, toda mutación exige la
/// cabecera "X-Api-Key" con su valor.
/// </summary>
public static class ApiKeyGuard
{
    private static readonly string? Clave = Environment.GetEnvironmentVariable("WEB_PANEL_API_KEY")?.Trim();

    /// <summary>¿La petición puede usar el panel? (clave correcta o sin clave configurada).</summary>
    public static bool Autorizado(HttpContext http)
    {
        if (string.IsNullOrEmpty(Clave)) return true;
        var enviada = http.Request.Headers["X-Api-Key"].ToString();
        return string.Equals(enviada, Clave, StringComparison.Ordinal);
    }
}
