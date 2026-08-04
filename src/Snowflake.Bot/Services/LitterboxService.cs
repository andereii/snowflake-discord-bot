using System.Net.Http.Headers;

namespace Snowflake.Bot.Services;

/// <summary>
/// Subida temporal de archivos a litterbox.catbox.moe (enlaces válidos 72 h).
/// Se usa cuando el archivo supera el límite de subida de Discord.
/// </summary>
public sealed class LitterboxService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private const string Endpoint = "https://litterbox.catbox.moe/resources/internals/api.php";

    /// <summary>Sube el archivo y devuelve la URL pública temporal.</summary>
    public async Task<string> SubirAsync(string filePath, string nombreAmigable, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("fileupload"), "reqtype");
        form.Add(new StringContent("72h"), "time");

        await using var fs = File.OpenRead(filePath);
        var fileContent = new StreamContent(fs)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") }
        };
        form.Add(fileContent, "fileToUpload", nombreAmigable);

        using var resp = await Http.PostAsync(Endpoint, form, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"litterbox respondió HTTP {(int)resp.StatusCode}: {body}");
        }

        var url = (await resp.Content.ReadAsStringAsync(ct)).Trim();
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Respuesta inesperada de litterbox: " + url);

        return url;
    }
}