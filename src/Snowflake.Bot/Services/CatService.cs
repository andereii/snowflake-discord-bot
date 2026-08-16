using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Snowflake.Bot.Services;

/// <summary>
/// Servicio para obtener fotos aleatorias de gatos desde The Cat API (con fallback a Cataas).
/// </summary>
public sealed class CatService(IHttpClientFactory httpFactory, ILogger<CatService> logger)
{
    private const string TheCatApiUrl = "https://api.thecatapi.com/v1/images/search";
    private const string CataasUrl = "https://cataas.com/cat?json=true";

    /// <summary>Obtiene la URL de una foto de gato aleatorio.</summary>
    public async Task<string?> ObtenerFotoGatoAsync(CancellationToken ct = default)
    {
        // Intento 1: The Cat API
        try
        {
            var http = httpFactory.CreateClient("CatApi");
            using var resp = await http.GetAsync(TheCatApiUrl, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var node = JsonNode.Parse(json);
                if (node is JsonArray array && array.Count > 0 && array[0]?["url"]?.GetValue<string>() is { } url)
                {
                    return url;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falló la llamada a The Cat API, intentando fallback de Cataas");
        }

        // Intento 2: Cataas (fallback)
        try
        {
            var http = httpFactory.CreateClient("CatApi");
            using var resp = await http.GetAsync(CataasUrl, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var node = JsonNode.Parse(json);
                if (node?["url"]?.GetValue<string>() is { } url)
                {
                    return url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? url
                        : $"https://cataas.com{url}";
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falló la llamada a Cataas fallback");
        }

        return null;
    }
}
