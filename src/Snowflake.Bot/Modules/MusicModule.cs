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
[SlashCommandGroup("m", "Música en canales de voz")]
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

    [SlashCommand("play", "Reproduce una canción o playlist (URL o búsqueda)")]
    public async Task PlayAsync(
        InteractionContext ctx,
        [Option("consulta", "URL de YouTube/Spotify o términos de búsqueda")] string consulta)
    {
        var voz = ctx.Member?.VoiceState?.Channel;
        if (voz is null)
        {
            await ResponderAsync(ctx, _msg.Get("Musica:NoEnCanal"), ephemeral: true);
            return;
        }

        await ctx.DeferAsync();

        try
        {
            var (resultado, puestaEnCola) = await _music.ReproducirAsync(ctx.Guild.Id, voz.Id, consulta);

            if (!resultado.IsSuccess)
            {
                await SafeEditAsync(ctx, _msg.Get("Musica:NoEncontrado"));
                return;
            }

            // Playlist: mensaje genérico de "playlist añadida".
            if (resultado.IsPlaylist)
            {
                await SafeEditAsync(ctx, _msg.Get("Musica:PlaylistAnadida",
                    ("titulo", resultado.Playlist?.Name ?? "Playlist"),
                    ("n", resultado.Count)));
            }
            else if (puestaEnCola)
            {
                // Ya había algo sonando: la pista fue a la cola. Lo decimos con su portada.
                var track = resultado.Track!;
                var embed = new DiscordEmbedBuilder()
                    .WithDescription(_msg.Get("Musica:PuestaEnCola",
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
                await SafeEditAsync(ctx, _msg.Get("Musica:Tocando",
                    ("titulo", track.Title),
                    ("autor", track.Author),
                    ("duracion", MusicService.FormatearDuracion(track.Duration, track.IsLiveStream))));

                await _widget.EnviarOActualizarAsync(ctx.Channel, ctx.Guild.Id);
            }
        }
        catch (Exception)
        {
            await SafeEditAsync(ctx, _msg.Get("Musica:ErrorLavalink"));
        }
    }

    [SlashCommand("skip", "Salta a la siguiente canción")]
    public async Task SkipAsync(InteractionContext ctx)
    {
        if (_music.Obtener(ctx.Guild.Id) is null)
        {
            await ResponderAsync(ctx, _msg.Get("Musica:NoActivo"), ephemeral: true);
            return;
        }
        if (!await PuedeControlarAsync(ctx)) return;

        var siguiente = await _music.SaltarAsync(ctx.Guild.Id);
        var texto = siguiente is null
            ? _msg.Get("Musica:SaltadoVacio")
            : _msg.Get("Musica:SaltadoProxima",
                ("titulo", siguiente.Title),
                ("autor", siguiente.Author));

        await ResponderAsync(ctx, texto);
        await _widget.RefrescarSiExisteAsync(ctx.Guild.Id, ctx.Channel);
    }

    [SlashCommand("cola", "Muestra la canción actual y la cola")]
    public async Task ColaAsync(InteractionContext ctx)
    {
        var embed = _music.ConstruirEmbedCola(ctx.Guild.Id, _msg);
        if (embed is null)
        {
            await ResponderAsync(ctx, _msg.Get("Musica:ColaVacia"), ephemeral: true);
            return;
        }
        await ResponderAsync(ctx, embed);
    }

    [SlashCommand("pausa", "Pausa la canción actual")]
    public async Task PausaAsync(InteractionContext ctx)
    {
        if (_music.Obtener(ctx.Guild.Id) is null)
        {
            await ResponderAsync(ctx, _msg.Get("Musica:NoActivo"), ephemeral: true);
            return;
        }
        if (!await PuedeControlarAsync(ctx)) return;

        await _music.PausarAsync(ctx.Guild.Id);
        await ResponderAsync(ctx, _msg.Get("Musica:Pausado"));
        await _widget.RefrescarSiExisteAsync(ctx.Guild.Id, ctx.Channel);
    }

    [SlashCommand("reanuda", "Reanuda la reproducción pausada")]
    public async Task ReanudaAsync(InteractionContext ctx)
    {
        if (_music.Obtener(ctx.Guild.Id) is null)
        {
            await ResponderAsync(ctx, _msg.Get("Musica:NoActivo"), ephemeral: true);
            return;
        }
        if (!await PuedeControlarAsync(ctx)) return;

        await _music.ReanudarAsync(ctx.Guild.Id);
        await ResponderAsync(ctx, _msg.Get("Musica:Reanudado"));
        await _widget.RefrescarSiExisteAsync(ctx.Guild.Id, ctx.Channel);
    }

    [SlashCommand("stop", "Detiene la música y desconecta al bot")]
    public async Task StopAsync(InteractionContext ctx)
    {
        if (!await PuedeControlarAsync(ctx)) return;

        await _music.DetenerAsync(ctx.Guild.Id);
        await _widget.DetenerAsync(ctx.Guild.Id);
        await ResponderAsync(ctx, _msg.Get("Musica:Detenido"));
    }

    [SlashCommand("volumen", "Cambia el volumen (0-100)")]
    public async Task VolumenAsync(
        InteractionContext ctx,
        [Option("nivel", "Nivel de volumen de 0 a 100")] long nivel)
    {
        var aplicado = await _music.VolumenAsync(ctx.Guild.Id, (int)nivel);
        await ResponderAsync(ctx, _msg.Get("Musica:Volumen", ("nivel", aplicado)));
    }

    // ------ auditoría de control de la reproducción ------

    /// <summary>
    /// ¿Puede este usuario controlar la música (pausar, saltar, detener)?
    /// Requisitos (cualquiera de ellos): permiso ManageGuild, rol DJ configurado
    /// o estar en el MISMO canal de voz que el bot. Si no, responde error.
    /// </summary>
    private async Task<bool> PuedeControlarAsync(InteractionContext ctx)
    {
        // ManageGuild (administradores) siempre pueden.
        if (ctx.Member is not null
            && ctx.Member.Permissions.HasPermission(Permissions.ManageGuild))
        {
            return true;
        }

        var djRoleId = (await _settings.GetAsync(ctx.Guild.Id)).DjRoleId;

        // Rol DJ del servidor, si está configurado.
        if (djRoleId is { } dj && ctx.Member is not null
            && ctx.Member.Roles.Any(r => r.Id == dj))
        {
            return true;
        }

        // Mismo canal de voz que el bot.
        var canalBot = ctx.Guild.CurrentMember?.VoiceState?.Channel;
        var canalUsuario = ctx.Member?.VoiceState?.Channel;
        if (canalBot is not null && canalUsuario is not null && canalBot.Id == canalUsuario.Id)
        {
            return true;
        }

        var mensaje = djRoleId is not null
            ? _msg.Get("Musica:RequiereDj", ("rol", $"<@&{djRoleId}>"))
            : _msg.Get("Musica:MismoCanal");
        await ResponderErrorAsync(ctx, mensaje);
        return false;
    }
}
