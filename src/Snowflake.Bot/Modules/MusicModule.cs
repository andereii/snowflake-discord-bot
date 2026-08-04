using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Lavalink4NET.Tracks;
using Snowflake.Bot.Services;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Reproducción de música en canales de voz mediante Lavalink.
/// Comandos: /m play /skip /cola /pausa /reanuda /stop /volumen
/// </summary>
[SlashCommandGroup("m", "Música en canales de voz")]
public sealed class MusicModule : ApplicationCommandModule
{
    private readonly MusicService _music;
    private readonly MusicWidgetService _widget;
    private readonly MessagesService _msg;

    public MusicModule(MusicService music, MusicWidgetService widget, MessagesService msg)
    {
        _music = music;
        _widget = widget;
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
        await ResponderAsync(ctx, "", embed);
    }

    [SlashCommand("pausa", "Pausa la canción actual")]
    public async Task PausaAsync(InteractionContext ctx)
    {
        if (_music.Obtener(ctx.Guild.Id) is null)
        {
            await ResponderAsync(ctx, _msg.Get("Musica:NoActivo"), ephemeral: true);
            return;
        }
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
        await _music.ReanudarAsync(ctx.Guild.Id);
        await ResponderAsync(ctx, _msg.Get("Musica:Reanudado"));
        await _widget.RefrescarSiExisteAsync(ctx.Guild.Id, ctx.Channel);
    }

    [SlashCommand("stop", "Detiene la música y desconecta al bot")]
    public async Task StopAsync(InteractionContext ctx)
    {
        await _music.DetenerAsync(ctx.Guild.Id);
        await _widget.DetenerAsync(ctx.Guild.Id);
        await ResponderAsync(ctx, _msg.Get("Musica:Detenido"));
    }

    [SlashCommand("volumen", "Cambia el volumen (0-100)")]
    public async Task VolumenAsync(
        InteractionContext ctx,
        [Option("nivel", "Nivel de volumen de 0 a 100")] long nivel)
    {
        var porcentaje = (int)Math.Clamp(nivel, 0L, 100L);
        var aplicado = await _music.VolumenAsync(ctx.Guild.Id, porcentaje);
        await ResponderAsync(ctx, _msg.Get("Musica:Volumen", ("nivel", aplicado)));
    }

    // ------ ayudantes ------

    private static async Task ResponderAsync(InteractionContext ctx, string contenido, bool ephemeral = false)
    {
        var b = new DiscordInteractionResponseBuilder().WithContent(contenido);
        if (ephemeral) b.AsEphemeral();
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, b);
    }

    private static async Task ResponderAsync(InteractionContext ctx, string _, DiscordEmbedBuilder embed)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(embed));
    }

    private static async Task SafeEditAsync(InteractionContext ctx, string contenido)
    {
        try { await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(contenido)); }
        catch { }
    }
}