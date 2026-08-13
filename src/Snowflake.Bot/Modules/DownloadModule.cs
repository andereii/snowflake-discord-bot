using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Microsoft.Extensions.Options;
using Snowflake.Bot.Configuration;
using Snowflake.Bot.Services;
using Snowflake.Bot.Services.Settings;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Descarga de vídeos o audio desde Internet (yt-dlp).
/// Soporta cualquier URL que yt-dlp entienda (YouTube, TikTok, Instagram, X, Reddit…).
/// Los archivos pequeños se adjuntan; los grandes se suben a litterbox (72h).
/// </summary>
public sealed class DownloadModule : SnowflakeModuleBase
{
    private readonly DownloadService _dl;
    private readonly LitterboxService _litter;
    private readonly GuildSettingsService _settings;
    private readonly MessagesService _msg;
    private readonly IOptionsMonitor<DownloadOptions> _dlOptions;
    private readonly IOptionsMonitor<BotConfiguration> _config;

    public DownloadModule(
        DownloadService dl,
        LitterboxService litter,
        GuildSettingsService settings,
        MessagesService msg,
        IOptionsMonitor<DownloadOptions> dlOptions,
        IOptionsMonitor<BotConfiguration> config)
    {
        _dl = dl;
        _litter = litter;
        _settings = settings;
        _msg = msg;
        _dlOptions = dlOptions;
        _config = config;
    }

    [SlashCommand("descargar", "Descarga un vídeo (o solo audio) de Internet con yt-dlp")]
    public async Task DescargarAsync(
        InteractionContext ctx,
        [Option("url", "URL del contenido a descargar")] string url,
        [Option("formato", "Qué descargar: el vídeo o solo el audio")]
        [Choice("Vídeo", "video"), Choice("Solo audio", "audio")]
        string formato = "video")
    {
        // Interruptor por servidor (desactivable desde el panel de configuración).
        if (!(await _settings.GetAsync(ctx.Guild.Id)).DownloadsEnabled)
        {
            await ResponderAsync(ctx, _msg.Get("Descargas:Desactivado"), ephemeral: true);
            return;
        }

        var soloAudio = formato == "audio";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme is not "http" and not "https"))
        {
            await ResponderAsync(ctx, _msg.Get("Descargas:UrlInvalida"), ephemeral: true);
            return;
        }

        // La descarga puede tardar varios segundos: defer para no agotar el timeout.
        await ctx.DeferAsync();

        string? tempDir = null;
        try
        {
            var timeout = TimeSpan.FromMinutes(Math.Max(1, _dlOptions.CurrentValue.TimeoutMinutes));
            using var cts = new CancellationTokenSource(timeout);
            var res = await _dl.DescargarAsync(url, soloAudio, cts.Token);
            tempDir = res.TempDir;

            var size = new FileInfo(res.FilePath).Length;
            var maxBytes = _dlOptions.CurrentValue.MaxDiscordBytes;

            if (size <= maxBytes)
            {
                // Adjuntar directamente al canal.
                await using var fs = File.OpenRead(res.FilePath);
                var builder = new DiscordWebhookBuilder()
                    .WithContent(_msg.Get("Descargas:Exito", ("titulo", res.Title)));
                builder.AddFile(Path.GetFileName(res.FilePath), fs);
                await ctx.EditResponseAsync(builder);
            }
            else
            {
                // Demasiado grande: subir a litterbox y responder con un enlace.
                var enlace = await _litter.SubirAsync(
                    res.FilePath, Path.GetFileName(res.FilePath), CancellationToken.None);

                var sizeMB = size / (1024.0 * 1024.0);
                var embed = new DiscordEmbedBuilder()
                    .WithTitle(res.Title)
                    .WithDescription(_msg.Get("Descargas:DemasiadoGrandeEmbed",
                        ("tamano", sizeMB.ToString("0.#")),
                        ("enlace", enlace)))
                    .WithUrl(enlace)
                    .WithColor(DiscordColor.Azure)
                    .WithFooter(_msg.Get("Descargas:PieLitterbox"));

                await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
            }
        }
        catch (YtDlpException ex)
        {
            var debug = _config.CurrentValue.Debug;
            var texto = debug
                ? _msg.Get("Descargas:Error", ("detalles", ex.Message))
                : _msg.Get("Descargas:ErrorGenerico");
            await SafeEditAsync(ctx, texto);
        }
        catch (Exception ex)
        {
            var debug = _config.CurrentValue.Debug;
            var texto = debug
                ? _msg.Get("Descargas:ErrorInterno", ("tipo", ex.GetType().Name), ("mensaje", ex.Message))
                : _msg.Get("Descargas:ErrorGenerico");
            await SafeEditAsync(ctx, texto);
        }
        finally
        {
            if (tempDir is not null)
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }
}
