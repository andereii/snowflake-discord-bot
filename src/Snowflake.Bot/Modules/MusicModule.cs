using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Lavalink4NET.Tracks;
using Snowflake.Bot.Services;
using Snowflake.Bot.Services.Settings;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Reproducción de música en canales de voz mediante Lavalink.
/// Comandos: /m play /skip /cola /pausa /reanuda /stop /volumen
///
/// Auditoría de seguridad: /m play está abierto a todos, pero los comandos de
/// CONTROL (pausa, saltar, reanudar, detener) exigen estar en el mismo canal de
/// voz que el bot, tener el rol DJ del servidor (si está configurado) o
/// permisos ManageGuild. Así nadie desde otro canal puede reventar la música.
/// </summary>
[SlashCommandGroup("m", "Music in voice channels")]
[DescriptionLocalization(Localization.Spanish, "Música en canales de voz")]
[DescriptionLocalization(Localization.Portuguese, "Música nos canais de voz")]
public sealed class MusicModule : SnowflakeModuleBase
{
    private readonly MusicService _music;
    private readonly MusicWidgetService _widget;
    private readonly GuildSettingsService _settings;
    private readonly MessagesService _msg;

    public MusicModule(
        MusicService music,
        MusicWidgetService widget,
        GuildSettingsService settings,
        MessagesService msg)
    {
        _music = music;
        _widget = widget;
        _settings = settings;
        _msg = msg;
    }

    [SlashCommand("play", "Play a song or playlist (URL or search)")]
    [NameLocalization(Localization.Spanish, "play")]
    [NameLocalization(Localization.Portuguese, "play")]
    [DescriptionLocalization(Localization.Spanish, "Reproduce una canción o playlist (URL o búsqueda)")]
    [DescriptionLocalization(Localization.Portuguese, "Toca uma música ou playlist (URL ou busca)")]
    public async Task PlayAsync(
        InteractionContext ctx,
        [Option("query", "YouTube/Spotify URL or search terms")]
        [NameLocalization(Localization.Spanish, "consulta")]
        [NameLocalization(Localization.Portuguese, "consulta")]
        [DescriptionLocalization(Localization.Spanish, "URL de YouTube/Spotify o términos de búsqueda")]
        [DescriptionLocalization(Localization.Portuguese, "URL do YouTube/Spotify ou termos de busca")] string consulta)
    {
        var voz = ctx.Member?.VoiceState?.Channel;
        if (voz is null)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Musica:NoEnCanal"), ephemeral: true);
            return;
        }

        await ctx.DeferAsync();

        if (!await _music.EstaOnlineAsync())
        {
            await SafeEditAsync(ctx, $"<:error:1534417252185800720> {_msg.Get(ctx.Guild.Id, "Musica:ErrorLavalinkOffline")}");
            return;
        }

        try
        {
            var (resultado, puestaEnCola) = await _music.ReproducirAsync(ctx.Guild.Id, voz.Id, consulta);

            if (!resultado.IsSuccess)
            {
                await SafeEditAsync(ctx, _msg.Get(ctx.Guild.Id, "Musica:NoEncontrado"));
                return;
            }

            // Playlist: mensaje genérico de "playlist añadida".
            if (resultado.IsPlaylist)
            {
                await SafeEditAsync(ctx, _msg.Get(ctx.Guild.Id, "Musica:PlaylistAnadida",
                    ("titulo", resultado.Playlist?.Name ?? "Playlist"),
                    ("n", resultado.Count)));
            }
            else if (puestaEnCola)
            {
                // Ya había algo sonando: la pista fue a la cola. Lo decimos con su portada.
                var track = resultado.Track!;
                var embed = new DiscordEmbedBuilder()
                    .WithDescription(_msg.Get(ctx.Guild.Id, "Musica:PuestaEnCola",
                        ("titulo", track.Title), ("autor", track.Author)))
                    .WithColor(DiscordColor.Blurple);

                var art = MusicService.ArtworkUrl(track);
                if (!string.IsNullOrEmpty(art))
                    embed.WithThumbnail(art);

                await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
            }
            else
            {
                // Empezando a sonar ahora: mensaje + widget.
                var track = resultado.Track!;
                await SafeEditAsync(ctx, _msg.Get(ctx.Guild.Id, "Musica:Tocando",
                    ("titulo", track.Title),
                    ("autor", track.Author),
                    ("duracion", MusicService.FormatearDuracion(track.Duration, track.IsLiveStream, _msg.Get(ctx.Guild.Id, "Musica:EnVivo")))));

                await _widget.EnviarOActualizarAsync(ctx.Channel, ctx.Guild.Id);
            }
        }
        catch (LavalinkUnavailableException)
        {
            await SafeEditAsync(ctx, $"<:error:1534417252185800720> {_msg.Get(ctx.Guild.Id, "Musica:ErrorLavalinkOffline")}");
        }
        catch (Exception)
        {
            await SafeEditAsync(ctx, _msg.Get(ctx.Guild.Id, "Musica:ErrorLavalink"));
        }
    }

    [SlashCommand("skip", "Skip to the next song")]
    [NameLocalization(Localization.Spanish, "skip")]
    [NameLocalization(Localization.Portuguese, "skip")]
    [DescriptionLocalization(Localization.Spanish, "Salta a la siguiente canción")]
    [DescriptionLocalization(Localization.Portuguese, "Pula para a próxima música")]
    public async Task SkipAsync(InteractionContext ctx)
    {
        if (_music.Obtener(ctx.Guild.Id) is null)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Musica:NoActivo"), ephemeral: true);
            return;
        }
        if (!await PuedeControlarAsync(ctx)) return;

        var siguiente = await _music.SaltarAsync(ctx.Guild.Id);
        var texto = siguiente is null
            ? _msg.Get(ctx.Guild.Id, "Musica:SaltadoVacio")
            : _msg.Get(ctx.Guild.Id, "Musica:SaltadoProxima",
                ("titulo", siguiente.Title),
                ("autor", siguiente.Author));

        await ResponderAsync(ctx, texto);
        await _widget.RefrescarSiExisteAsync(ctx.Guild.Id, ctx.Channel);
    }

    [SlashCommand("queue", "Show the current song and the queue")]
    [NameLocalization(Localization.Spanish, "cola")]
    [NameLocalization(Localization.Portuguese, "fila")]
    [DescriptionLocalization(Localization.Spanish, "Muestra la canción actual y la cola")]
    [DescriptionLocalization(Localization.Portuguese, "Mostra a música atual e a fila")]
    public async Task ColaAsync(InteractionContext ctx)
    {
        var embed = _music.ConstruirEmbedCola(ctx.Guild.Id, _msg);
        if (embed is null)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Musica:ColaVacia"), ephemeral: true);
            return;
        }
        await ResponderAsync(ctx, embed);
    }

    [SlashCommand("pause", "Pause the current song")]
    [NameLocalization(Localization.Spanish, "pausa")]
    [NameLocalization(Localization.Portuguese, "pausar")]
    [DescriptionLocalization(Localization.Spanish, "Pausa la canción actual")]
    [DescriptionLocalization(Localization.Portuguese, "Pausa a música atual")]
    public async Task PausaAsync(InteractionContext ctx)
    {
        if (_music.Obtener(ctx.Guild.Id) is null)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Musica:NoActivo"), ephemeral: true);
            return;
        }
        if (!await PuedeControlarAsync(ctx)) return;

        await _music.PausarAsync(ctx.Guild.Id);
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Musica:Pausado"));
        await _widget.RefrescarSiExisteAsync(ctx.Guild.Id, ctx.Channel);
    }

    [SlashCommand("resume", "Resume the paused playback")]
    [NameLocalization(Localization.Spanish, "reanuda")]
    [NameLocalization(Localization.Portuguese, "retomar")]
    [DescriptionLocalization(Localization.Spanish, "Reanuda la reproducción pausada")]
    [DescriptionLocalization(Localization.Portuguese, "Retoma a reprodução pausada")]
    public async Task ReanudaAsync(InteractionContext ctx)
    {
        if (_music.Obtener(ctx.Guild.Id) is null)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Musica:NoActivo"), ephemeral: true);
            return;
        }
        if (!await PuedeControlarAsync(ctx)) return;

        await _music.ReanudarAsync(ctx.Guild.Id);
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Musica:Reanudado"));
        await _widget.RefrescarSiExisteAsync(ctx.Guild.Id, ctx.Channel);
    }

    [SlashCommand("stop", "Stop the music and disconnect the bot")]
    [NameLocalization(Localization.Spanish, "stop")]
    [NameLocalization(Localization.Portuguese, "stop")]
    [DescriptionLocalization(Localization.Spanish, "Detiene la música y desconecta al bot")]
    [DescriptionLocalization(Localization.Portuguese, "Para a música e desconecta o bot")]
    public async Task StopAsync(InteractionContext ctx)
    {
        if (!await PuedeControlarAsync(ctx)) return;

        await _music.DetenerAsync(ctx.Guild.Id);
        await _widget.DetenerAsync(ctx.Guild.Id);
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Musica:Detenido"));
    }

    [SlashCommand("volume", "Change the volume (0-100)")]
    [NameLocalization(Localization.Spanish, "volumen")]
    [NameLocalization(Localization.Portuguese, "volume")]
    [DescriptionLocalization(Localization.Spanish, "Cambia el volumen (0-100)")]
    [DescriptionLocalization(Localization.Portuguese, "Muda o volume (0-100)")]
    public async Task VolumenAsync(
        InteractionContext ctx,
        [Option("level", "Volume level from 0 to 100")]
        [NameLocalization(Localization.Spanish, "nivel")]
        [NameLocalization(Localization.Portuguese, "nível")]
        [DescriptionLocalization(Localization.Spanish, "Nivel de volumen de 0 a 100")]
        [DescriptionLocalization(Localization.Portuguese, "Nível de volume de 0 a 100")] long nivel)
    {
        var aplicado = await _music.VolumenAsync(ctx.Guild.Id, (int)nivel);
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Musica:Volumen", ("nivel", aplicado)));
    }

    /// <summary>
    /// Comando temporal de diagnóstico de Lavalink (se eliminará antes del release).
    /// </summary>
    [SlashCommand("status", "Check the Lavalink music server connection status")]
    [NameLocalization(Localization.Spanish, "estado")]
    [NameLocalization(Localization.Portuguese, "status")]
    [DescriptionLocalization(Localization.Spanish, "Comprueba el estado de la conexión con el servidor Lavalink")]
    [DescriptionLocalization(Localization.Portuguese, "Verifica o status da conexão com o servidor Lavalink")]
    public async Task StatusAsync(InteractionContext ctx)
    {
        await ctx.DeferAsync();
        var embed = await _music.ConstruirEmbedEstadoLavalinkAsync(ctx.Guild.Id, _msg);
        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
    }

    // ------ auditoría de control de la reproducción ------

    /// <summary>
    /// ¿Puede este usuario controlar la música (pausar, saltar, detener)?
    /// Requisitos (cualquiera de ellos): permiso ManageGuild, rol DJ configurado
    /// o estar en el MISMO canal de voz que el bot. Si no, responde error.
    /// </summary>
    private async Task<bool> PuedeControlarAsync(InteractionContext ctx)
    {
        var (puede, mensaje) = await _music.ValidarControlAsync(ctx.Guild, ctx.Member, _msg);
        if (!puede)
        {
            await ResponderErrorAsync(ctx, mensaje!);
            return false;
        }
        return true;
    }
}
