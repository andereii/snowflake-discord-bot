using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Snowflake.Bot.Services;

/// <summary>
/// Resultado de una descarga: el archivo final, su título legible y el
/// directorio temporal que lo contiene (para limpiar después).
/// </summary>
public sealed record DownloadResult(string FilePath, string Title, string TempDir);

/// <summary>
/// Excepción lanzada cuando yt-dlp falla (URL no soportada, privado, etc.).
/// Su Message contiene los últimos detalles que devolvió yt-dlp.
/// </summary>
public sealed class YtDlpException(string detalles) : Exception(detalles);

/// <summary>
/// Descarga vídeos/audio usando yt-dlp como proceso externo.
/// Soporta cualquier URL que yt-dlp entienda (YouTube, TikTok, Instagram, X, Reddit…).
/// </summary>
public sealed class DownloadService(ILogger<DownloadService> logger)
{
    private static readonly string CookiesFile =
        Environment.GetEnvironmentVariable("YT_COOKIES_FILE") ?? string.Empty;

    /// <summary>
    /// Descarga el contenido de la URL. Si <paramref name="soloAudio"/> es true,
    /// lo extrae a MP3. Deja el archivo en un directorio temporal que el llamador
    /// debe borrar cuando termine.
    /// </summary>
    public async Task<DownloadResult> DescargarAsync(
        string url, bool soloAudio, CancellationToken externalCt)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "snowflake", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var template = Path.Combine(tempDir, "%(title).80B [%(id)s].%(ext)s");

            var psi = new ProcessStartInfo("yt-dlp")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            psi.ArgumentList.Add("--no-playlist");
            psi.ArgumentList.Add("--no-progress");
            psi.ArgumentList.Add("--no-warnings");
            psi.ArgumentList.Add("--no-part");
            psi.ArgumentList.Add("--restrict-filenames");
            psi.ArgumentList.Add("--print");
            psi.ArgumentList.Add("after_move:filepath");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(template);

            if (!string.IsNullOrWhiteSpace(CookiesFile) && File.Exists(CookiesFile))
            {
                psi.ArgumentList.Add("--cookies");
                psi.ArgumentList.Add(CookiesFile);
            }

            if (soloAudio)
            {
                psi.ArgumentList.Add("-x");
                psi.ArgumentList.Add("--audio-format");
                psi.ArgumentList.Add("mp3");
                psi.ArgumentList.Add("--audio-quality");
                psi.ArgumentList.Add("0");
            }

            psi.ArgumentList.Add(url);

            using var proc = new Process { StartInfo = psi };
            if (!proc.Start())
                throw new InvalidOperationException("No se pudo iniciar yt-dlp.");

            // Tope duro: 5 minutos. Enlazado con el token externo (p. ej. cancelación).
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(externalCt, timeoutCts.Token);

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(linked.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(linked.Token);

            try
            {
                await proc.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                throw new YtDlpException("La descarga tardó demasiado y se canceló.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (proc.ExitCode != 0)
            {
                var detalles = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                logger.LogWarning("yt-dlp falló (código {Codigo}): {Detalles}", proc.ExitCode, detalles);
                throw new YtDlpException(Sanitize(detalles));
            }

            var filePath = stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault(l => !string.IsNullOrWhiteSpace(l));

            if (filePath is null || !File.Exists(filePath))
            {
                var files = Directory.GetFiles(tempDir);
                if (files.Length == 0)
                    throw new YtDlpException("No se generó ningún archivo tras la descarga.");
                filePath = files[0];
            }

            var title = Path.GetFileNameWithoutExtension(filePath);
            return new DownloadResult(filePath, title, tempDir);
        }
        catch
        {
            // En error se limpia el directorio temporal; en éxito lo limpia el llamador.
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            throw;
        }
    }

    private static string Sanitize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "Error desconocido de yt-dlp.";
        var lineas = s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var resumen = string.Join(" | ", lineas.TakeLast(3));
        return resumen.Length > 800 ? resumen[..800] + "…" : resumen;
    }
}