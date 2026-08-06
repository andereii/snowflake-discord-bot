using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Snowflake.Bot.Data;

namespace Snowflake.Bot.Services;

/// <summary>
/// Vigila los canales de YouTube suscritos por los servidores mediante el feed
/// RSS público (sin clave de API) y avisa en Discord cuando se sube un vídeo
/// nuevo. Un feed por canal (agrupa varios servidores que siguen el mismo canal).
/// </summary>
public sealed partial class YouTubeNotifyService : BackgroundService
{
    // El feed público de YouTube se actualiza ~cada 15 minutos y no requiere clave.
    private const string FeedUrl = "https://www.youtube.com/feeds/videos.xml?channel_id={0}";

    // Namespace de yt:videoId dentro del feed.
    private static readonly XNamespace YtNs = "http://www.youtube.com/xml/schemas/2015";
    private static readonly XNamespace AtomNs = "http://www.w3.org/2005/Atom";

    private readonly IServiceProvider _services;
    private readonly DiscordClient _client;
    private readonly IHttpClientFactory _httpFactory;
    private readonly MessagesService _msg;
    private readonly ILogger<YouTubeNotifyService> _logger;

    public YouTubeNotifyService(
        IServiceProvider services,
        DiscordClient client,
        IHttpClientFactory httpFactory,
        MessagesService msg,
        ILogger<YouTubeNotifyService> logger)
    {
        _services = services;
        _client = client;
        _httpFactory = httpFactory;
        _msg = msg;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("YouTubeNotifyService iniciado (polling cada 5 minutos).");

        // Espera inicial corta para que el bot se conecte.
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RevisarAsync(stoppingToken).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogError(ex, "Error en el bucle de YouTube Notify"); }

            try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task RevisarAsync(CancellationToken ct)
    {
        var dbFactory = _services.GetRequiredService<IDbContextFactory<BotDbContext>>();
        List<Data.Entities.YouTubeSubscription> subs;
        await using (var db0 = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
        {
            subs = await db0.YouTubeSubscriptions.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        }
        if (subs.Count == 0) return;

        // Agrupa por channel_id -> feed único.
        var porCanal = subs.GroupBy(s => s.YTChannelId).ToList();

        var http = _httpFactory.CreateClient("YouTube");

        foreach (var grupo in porCanal)
        {
            if (ct.IsCancellationRequested) return;

            var channelId = grupo.Key;
            List<FeedVideo>? recientes = null;
            try { recientes = await ObtenerFeedAsync(http, channelId, ct).ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo leer el feed de YouTube para {Channel}", channelId);
            }

            if (recientes is null || recientes.Count == 0) continue;

            foreach (var sub in grupo)
            {
                var nuevo = EncontrarPrimerNuevo(recientes, sub.LastVideoId);
                if (nuevo == null) continue;

                // Backfill en la primera pasada: marca el vídeo más reciente como
                // último visto y NO notifica. En el siguiente ciclo, si hay uno
                // más nuevo, se avisa.
                var fueBackfill = string.IsNullOrEmpty(sub.LastVideoId);
                await NotificarOActualizarAsync(sub, nuevo, esBackfill: fueBackfill, ct);
            }
        }
    }

    private static FeedVideo? EncontrarPrimerNuevo(List<FeedVideo> recientes, string? lastVideoId)
    {
        // El feed viene en orden: el más reciente el primero.
        var primero = recientes[0];
        if (string.IsNullOrEmpty(lastVideoId))
            return primero; // backfill
        if (primero.VideoId == lastVideoId)
            return null;

        // Precaución: si por algún motivo el último visto ya no está en el feed
        // (el canal lleva mucho sin subir y hay vídeos nuevos), avisamos solo del
        // más reciente para no spurtear el historial guardado.
        return primero;
    }

    private async Task<List<FeedVideo>> ObtenerFeedAsync(HttpClient http, string channelId, CancellationToken ct)
    {
        var url = string.Format(CultureInfo.InvariantCulture, FeedUrl, channelId);
        using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return new List<FeedVideo>();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct).ConfigureAwait(false);

        var entries = doc.Root?.Elements(AtomNs + "entry") ?? Enumerable.Empty<XElement>();
        var videos = new List<FeedVideo>();
        foreach (var entry in entries)
        {
            var videoId = entry.Element(YtNs + "videoId")?.Value;
            var title = entry.Element(AtomNs + "title")?.Value;
            var author = entry.Element(AtomNs + "author")?.Element(AtomNs + "name")?.Value;
            var published = entry.Element(AtomNs + "published")?.Value;
            var link = entry.Element(AtomNs + "link")?.Attribute("href")?.Value;

            if (string.IsNullOrEmpty(videoId)) continue;
            var dt = DateTimeOffset.TryParse(published, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var d) ? d : DateTimeOffset.UtcNow;
            videos.Add(new FeedVideo(videoId!, title ?? "", author ?? "", link ?? $"https://www.youtube.com/watch?v={videoId}", dt));
        }
        return videos;
    }

    private async Task NotificarOActualizarAsync(
        Data.Entities.YouTubeSubscription sub,
        FeedVideo nuevo,
        bool esBackfill,
        CancellationToken ct)
    {
        var dbFactory = _services.GetRequiredService<IDbContextFactory<BotDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var actual = await db.YouTubeSubscriptions.FindAsync(sub.GuildId);
        if (actual is null) return;

        if (esBackfill)
        {
            // Marca el más reciente como visto sin notificar. Solo avisaremos a
            // partir de los próximos.
            actual.LastVideoId = nuevo.VideoId;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        // Notificación real: envía luego actualiza la marca de agua.
        try
        {
            if (_client.Guilds.TryGetValue(sub.GuildId, out var guild))
            {
                var canal = guild.GetChannel(sub.NotifyChannelId);
                if (canal is not null)
                    await EnviarNotificacionAsync(canal, sub, nuevo);
                else
                    _logger.LogWarning("Canal de notificación {Canal} ya no existe en {Guild}",
                        sub.NotifyChannelId, sub.GuildId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo enviar la notificación de YouTube a {Guild}", sub.GuildId);
        }

        actual.LastVideoId = nuevo.VideoId;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task EnviarNotificacionAsync(
        DiscordChannel canal, Data.Entities.YouTubeSubscription sub, FeedVideo v)
    {
        var subidoRel = ConstruirRelativo(v.Published);

        // Texto previo (mensaje personalizado o por defecto).
        var texto = ConstruirTextoNotificacion(sub, v, subidoRel);

        // Embed con la info + miniatura (siempre debajo del texto, enriquecido
        // y con el enlace).
        var embed = new DiscordEmbedBuilder()
            .WithTitle(v.Title)
            .WithUrl(v.Link)
            .WithDescription($"**{v.Author}**")
            .WithColor(DiscordColor.Red)
            .WithThumbnail($"https://i.ytimg.com/vi/{v.VideoId}/hqdefault.jpg")
            .WithTimestamp(v.Published)
            .Build();

        var builder = new DiscordMessageBuilder()
            .WithContent(texto)
            .AddEmbed(embed);

        // Mención de rol opcional. Usar ParseDefaults para que no haga ping
        // involuntario de otros roles/usuarios.
        if (sub.NotifyRoleId is { } rolId)
        {
            var rolMention = canal.Guild.GetRole(rolId);
            if (rolMention is not null)
            {
                builder.WithContent($"{texto} {rolMention.Mention}");
                // Permitir el ping (por defecto DSharpPlus suprime pings no listados).
                builder.AddMention(new RoleMention(rolMention));
            }
        }

        try { await canal.SendMessageAsync(builder); }
        catch (Exception ex) { _logger.LogWarning(ex, "No se pudo publicar la notificación de YouTube en {Canal}", canal.Id); }
    }

    private string ConstruirTextoNotificacion(
        Data.Entities.YouTubeSubscription sub, FeedVideo v, string subidoRel)
    {
        // Plantilla personalizada o por defecto.
        var base_ = string.IsNullOrWhiteSpace(sub.CustomMessage)
            ? _msg.Get("YouTube:NotiPorDefecto")
            : sub.CustomMessage;

        var texto = base_
            .Replace("{canal}", v.Author)
            .Replace("{autor}", v.Author)
            .Replace("{titulo}", v.Title)
            .Replace("{url}", v.Link)
            .Replace("{videoId}", v.VideoId)
            .Replace("{subido}", v.Published.ToString("o", CultureInfo.InvariantCulture))
            .Replace("{subidoREL}", subidoRel);

        // Decisión de diseño: el enlace se envía SIEMPRE después del mensaje (se
        // junta el enlace al final si no estaba ya en la plantilla).
        if (!texto.Contains(v.Link))
            texto = $"{texto}\n{v.Link}";

        // Trunca a 1900 para no pasarse del límite de mensaje.
        return texto.Length > 1900 ? texto[..1899] + "…" : texto;
    }

    internal static string ConstruirRelativo(DateTimeOffset dt)
    {
        var delta = DateTimeOffset.UtcNow - dt;
        if (delta < TimeSpan.FromMinutes(1)) return "hace un momento";
        if (delta < TimeSpan.FromHours(1)) return $"hace {(int)delta.TotalMinutes} minuto(s)";
        if (delta < TimeSpan.FromDays(1)) return $"hace {(int)delta.TotalHours} hora(s)";
        if (delta < TimeSpan.FromDays(30)) return $"hace {(int)delta.TotalDays} día(s)";
        return dt.ToString("d", CultureInfo.InvariantCulture);
    }

    // ---------------------- Resolución de channel_id ----------------------

    /// <summary>
    /// Resuelve una URL o @handle de YouTube a su channel_id (UC...). Usa yt-dlp
    /// con --print channel_id (ya instalado en el sistema).
    /// </summary>
    public static async Task<(string ChannelId, string ChannelName)?> ResolverCanalAsync(
        string entrada, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(entrada)) return null;

        // Acepta "@handle", "/c/nombre", "/user/nombre", "/channel/UC...", URLs completas.
        var arg = entrada.Trim();
        if (arg.StartsWith('@'))
            arg = "https://www.youtube.com/" + arg;

        try
        {
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
            psi.ArgumentList.Add("--no-warnings");
            psi.ArgumentList.Add("--print");
            psi.ArgumentList.Add("channel_id");
            psi.ArgumentList.Add(arg);

            using var proc = new Process { StartInfo = psi };
            if (!proc.Start()) return null;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);
            try { await proc.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (proc.ExitCode != 0)
            {
                logger.LogWarning("yt-dlp falló al resolver canal: {Detalles}", stderr);
                return null;
            }

            var channelId = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(l => l.StartsWith("UC", StringComparison.Ordinal));
            if (string.IsNullOrEmpty(channelId)) return null;

            // Nombre del canal: pedimos también el channel con --print.
            var nombre = await ObtenerNombreCanalAsync(channelId, logger);
            return (channelId, nombre ?? channelId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Excepción resolviendo canal de YouTube");
            return null;
        }
    }

    private static async Task<string?> ObtenerNombreCanalAsync(string channelId, ILogger logger)
    {
        try
        {
            var psi = new ProcessStartInfo("yt-dlp")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("--no-playlist");
            psi.ArgumentList.Add("--no-warnings");
            psi.ArgumentList.Add("--print");
            psi.ArgumentList.Add("channel");
            psi.ArgumentList.Add($"https://www.youtube.com/channel/{channelId}");

            using var proc = new Process { StartInfo = psi };
            if (!proc.Start()) return null;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            try { await proc.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            var stdout = await stdoutTask;
            var nombre = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            return string.IsNullOrEmpty(nombre) ? null : nombre;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "No se pudo obtener el nombre del canal {Id}", channelId);
            return null;
        }
    }

    // ---------------------- DTO interno ----------------------

    internal sealed record FeedVideo(
        string VideoId,
        string Title,
        string Author,
        string Link,
        DateTimeOffset Published);
}