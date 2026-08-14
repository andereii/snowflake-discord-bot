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

    [SlashCommand("download", "Download a video (or audio only) from the internet with yt-dlp")]
    [NameLocalization(Localization.Spanish, "descargar")]
    [NameLocalization(Localization.Portuguese, "baixar")]
    [DescriptionLocalization(Localization.Spanish, "Descarga un vídeo (o solo audio) de Internet con yt-dlp")]
    [DescriptionLocalization(Localization.Portuguese, "Baixa um vídeo (ou só o áudio) da internet com yt-dlp")]
    public async Task DescargarAsync(
        InteractionContext ctx,
        [Option("url", "URL of the content to download")]
        [NameLocalization(Localization.Spanish, "url")]
        [NameLocalization(Localization.Portuguese, "url")]
        [DescriptionLocalization(Localization.Spanish, "URL del contenido a descargar")]
        [DescriptionLocalization(Localization.Portuguese, "URL do conteúdo a baixar")] string url,
        [Option("format", "What to download: the video or audio only")]
        [NameLocalization(Localization.Spanish, "formato")]
        [NameLocalization(Localization.Portuguese, "formato")]
        [DescriptionLocalization(Localization.Spanish, "Qué descargar: el vídeo o solo el audio")]
        [DescriptionLocalization(Localization.Portuguese, "O que baixar: o vídeo ou só o áudio")]
        [Choice("Video", "video"), Choice("Audio only", "audio")]
        string formato = "video")
    {
        // Interruptor por servidor (desactivable desde el panel de configuración).
        if (!(await _settings.GetAsync(ctx.Guild.Id)).DownloadsEnabled)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Descargas:Desactivado"), ephemeral: true);
            return;
        }

        var soloAudio = formato == "audio";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme is not "http" and not "https"))
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Descargas:UrlInvalida"), ephemeral: true);
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
                    .WithContent(_msg.Get(ctx.Guild.Id, "Descargas:Exito", ("titulo", res.Title)));
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
                    .WithDescription(_msg.Get(ctx.Guild.Id, "Descargas:DemasiadoGrandeEmbed",
                        ("tamano", sizeMB.ToString("0.#")),
                        ("enlace", enlace)))
                    .WithUrl(enlace)
                    .WithColor(DiscordColor.Azure)
                    .WithFooter(_msg.Get(ctx.Guild.Id, "Descargas:PieLitterbox"));

                await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
            }
        }
        catch (YtDlpException ex)
        {
            var debug = _config.CurrentValue.Debug;
            var texto = debug
                ? _msg.Get(ctx.Guild.Id, "Descargas:Error", ("detalles", ex.Message))
                : _msg.Get(ctx.Guild.Id, "Descargas:ErrorGenerico");
            await SafeEditAsync(ctx, texto);
        }
        catch (Exception ex)
        {
            var debug = _config.CurrentValue.Debug;
            var texto = debug
                ? _msg.Get(ctx.Guild.Id, "Descargas:ErrorInterno", ("tipo", ex.GetType().Name), ("mensaje", ex.Message))
                : _msg.Get(ctx.Guild.Id, "Descargas:ErrorGenerico");
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
