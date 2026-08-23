using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Snowflake.Bot.Services;

/// <summary>
/// Servicio de traducción de textos a cualquier idioma mediante Google Translate (GTX)
/// con decodificación de entidades HTML y caché en memoria.
/// </summary>
public sealed class TranslationService
{
    private readonly HttpClient _http;
    private readonly ILogger<TranslationService> _logger;
    private readonly ConcurrentDictionary<string, (string Traduccion, DateTimeOffset Expira)> _cache = new();

    public TranslationService(HttpClient http, ILogger<TranslationService> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Traduce un texto al idioma destino (ej. "es", "pt", "en").
    /// </summary>
    public async Task<string> TraducirAsync(string? texto, string targetLang, string sourceLang = "auto")
    {
        if (string.IsNullOrWhiteSpace(texto)) return texto ?? string.Empty;

        texto = WebUtility.HtmlDecode(texto).Trim();
        targetLang = targetLang.ToLowerInvariant();
        if (targetLang is "en" && (sourceLang is "en" || sourceLang is "auto"))
            return texto;

        var claveCache = $"{sourceLang}:{targetLang}:{texto}";
        if (_cache.TryGetValue(claveCache, out var entry) && entry.Expira > DateTimeOffset.UtcNow)
        {
            return entry.Traduccion;
        }

        try
        {
            var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sourceLang}&tl={targetLang}&dt=t&q={Uri.EscapeDataString(texto)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            using var resp = await _http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Error HTTP en traducción de Google ({Status})", resp.StatusCode);
                return texto;
            }

            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var sentences = root[0];
                if (sentences.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var s in sentences.EnumerateArray())
                    {
                        if (s.ValueKind == JsonValueKind.Array && s.GetArrayLength() > 0)
                        {
                            var part = s[0].GetString();
                            if (!string.IsNullOrEmpty(part))
                                sb.Append(part);
                        }
                    }

                    var resultado = WebUtility.HtmlDecode(sb.ToString().Trim());
                    if (!string.IsNullOrEmpty(resultado))
                    {
                        _cache[claveCache] = (resultado, DateTimeOffset.UtcNow.AddHours(24));
                        return resultado;
                    }
                }
            }

            return texto;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Excepción traduciendo texto a {Lang}", targetLang);
            return texto;
        }
    }

    /// <summary>
    /// Traduce múltiples textos en paralelo al idioma indicado.
    /// </summary>
    public async Task<List<string>> TraducirLoteAsync(IReadOnlyList<string> textos, string targetLang, string sourceLang = "auto")
    {
        var tareas = textos.Select(t => TraducirAsync(t, targetLang, sourceLang));
        var resultados = await Task.WhenAll(tareas).ConfigureAwait(false);
        return [.. resultados];
    }
}
