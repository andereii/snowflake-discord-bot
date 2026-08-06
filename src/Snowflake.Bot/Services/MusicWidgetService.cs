using System.Collections.Concurrent;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.Logging;

namespace Snowflake.Bot.Services;

/// <summary>
/// Widget "reproduciendo ahora": un mensaje con portada, duración y botones de
/// control. No se actualiza solo; solo se refresca ante acciones del usuario
/// (pausar, saltar, añadir canción…). Los botones funcionan por interacción.
/// </summary>
public sealed class MusicWidgetService(
    DiscordClient client,
    IPlayerManager players,
    MusicService music,
    MessagesService msg,
    ILogger<MusicWidgetService> logger)
{
    public const string CustomIdPause = "snowflake_music_pause";
    public const string CustomIdSkip = "snowflake_music_skip";
    public const string CustomIdStop = "snowflake_music_stop";
    public const string CustomIdCola = "snowflake_music_cola";

    private static readonly string[] MusicIds = [CustomIdPause, CustomIdSkip, CustomIdStop, CustomIdCola];

    private sealed record Widget(ulong MessageId, ulong ChannelId);
    private readonly ConcurrentDictionary<ulong, Widget> _widgets = new();

    /// <summary>Crea (o reemplaza) el widget en el canal indicado.</summary>
    public async Task EnviarOActualizarAsync(DiscordChannel canal, ulong guildId)
    {
        ulong? mensajeId = null;
        if (_widgets.TryRemove(guildId, out var viejo))
            mensajeId = viejo.MessageId;

        var embed = ConstruirEmbed(guildId);
        var botones = ConstruirBotones();

        DiscordMessage? mensaje = null;
        if (mensajeId is not null)
        {
            try { mensaje = await canal.GetMessageAsync(mensajeId.Value); }
            catch { mensaje = null; }
        }

        if (mensaje is not null)
        {
            try { await mensaje.ModifyAsync(new DiscordMessageBuilder().WithEmbed(embed).AddComponents(botones)); }
            catch { mensaje = null; }
        }
        if (mensaje is null)
            mensaje = await canal.SendMessageAsync(new DiscordMessageBuilder().WithEmbed(embed).AddComponents(botones));

        _widgets[guildId] = new Widget(mensaje.Id, canal.Id);
    }

    /// <summary>Refresca el widget existente (si lo hay). Nop si no hay.</summary>
    public async Task RefrescarSiExisteAsync(ulong guildId, DiscordChannel canal)
    {
        if (!_widgets.TryGetValue(guildId, out var w)) return;
        try
        {
            var mensaje = await canal.GetMessageAsync(w.MessageId);
            await mensaje.ModifyAsync(new DiscordMessageBuilder()
                .WithEmbed(ConstruirEmbed(guildId))
                .AddComponents(ConstruirBotones()));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "No se pudo refrescar el widget de música {Guild}", guildId);
        }
    }

    /// <summary>
    /// Finaliza el widget: lo deja 5 segundos con el mensaje "Reproducción
    /// detenida" y botones deshabilitados, y después lo borra del canal.
    /// El borrado se hace en background (fire-and-forget) para no bloquear al
    /// llamador (p. ej. el handler del botón Stop).
    /// </summary>
    public Task DetenerAsync(ulong guildId)
    {
        if (!_widgets.TryRemove(guildId, out var w)) return Task.CompletedTask;
        _ = BorrarWidgetTrasRetrasoAsync(guildId, w);
        return Task.CompletedTask;
    }

    private async Task BorrarWidgetTrasRetrasoAsync(ulong guildId, Widget w)
    {
        try
        {
            if (!client.Guilds.TryGetValue(guildId, out var g)) return;
            if (g.GetChannel(w.ChannelId) is not { } canal) return;

            var mensaje = await canal.GetMessageAsync(w.MessageId);
            await mensaje.ModifyAsync(new DiscordMessageBuilder()
                .WithEmbed(new DiscordEmbedBuilder()
                    .WithDescription(msg.Get("Musica:ReproduccionDetenida"))
                    .WithColor(DiscordColor.Grayple))
                .AddComponents(ConstruirBotones(deshabilitados: true)));

            await Task.Delay(5000);
            await mensaje.DeleteAsync();
        }
        catch { /* mensaje borrado, canal desaparecido, etc. */ }
    }

    /// <summary>Maneja los botones del widget (pausa/reanuda, saltar, detener, cola).</summary>
    public async Task HandleButtonAsync(ComponentInteractionCreateEventArgs e)
    {
        if (e.Guild is null || !MusicIds.Contains(e.Id)) return;
        var canal = e.Channel;

        try
        {
            // Cola: respuesta efímera con la cola (no toca el reproductor).
            if (e.Id == CustomIdCola)
            {
                var embedCola = music.ConstruirEmbedCola(e.Guild.Id, msg);
                var builder = embedCola is null
                    ? new DiscordInteractionResponseBuilder().WithContent(msg.Get("Musica:ColaVacia"))
                    : new DiscordInteractionResponseBuilder().AddEmbed(embedCola);
                await e.Interaction.CreateResponseAsync(
                    InteractionResponseType.ChannelMessageWithSource, builder.AsEphemeral());
                return;
            }

            if (!players.TryGetPlayer<IQueuedLavalinkPlayer>(e.Guild.Id, out var p) || p is null)
            {
                await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
                return;
            }

            if (e.Id == CustomIdPause)
            {
                if (((LavalinkPlayer)p).IsPaused) await p.ResumeAsync(default);
                else await p.PauseAsync(default);
            }
            else if (e.Id == CustomIdSkip)
            {
                await p.SkipAsync(1, default);
            }
            else if (e.Id == CustomIdStop)
            {
                try { await p.StopAsync(default); } catch { }
                try { await p.DisconnectAsync(default); } catch { }
                await DetenerAsync(e.Guild.Id);
                // DetenerAsync ya hace el "reproducción detenida" + borrado; aquí solo
                // confirmamos la interacción sin volver a tocar el mensaje (se borrará).
                await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
                return;
            }

            // Para pausa/skip: actualiza el widget in situ (sirve de ACK).
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder()
                    .AddEmbed(ConstruirEmbed(e.Guild.Id))
                    .AddComponents(ConstruirBotones()));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error en botón del widget de música");
            try { await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate); }
            catch { }
        }
    }

    private DiscordEmbed ConstruirEmbed(ulong guildId)
    {
        if (!players.TryGetPlayer<IQueuedLavalinkPlayer>(guildId, out var p) || p is null)
            return new DiscordEmbedBuilder().WithDescription(msg.Get("Musica:NoActivo")).Build();

        var track = p.CurrentTrack;
        if (track is null)
            return new DiscordEmbedBuilder().WithDescription(msg.Get("Musica:WidgetSinPista")).Build();

        var builder = new DiscordEmbedBuilder()
            .WithTitle(msg.Get("Musica:WidgetTitulo"))
            .WithDescription($"**[{track.Title}]({track.Uri})**\n{track.Author}")
            .WithColor(DiscordColor.Blurple);

        var artwork = MusicService.ArtworkUrl(track);
        if (!string.IsNullOrEmpty(artwork))
            builder.WithThumbnail(artwork);

        var estado = ((LavalinkPlayer)p).IsPaused
            ? msg.Get("Musica:EstadoPausado")
            : msg.Get("Musica:EstadoReproduciendo");

        builder.AddField(msg.Get("Musica:WidgetEstado"), estado, true);
        builder.AddField(msg.Get("Musica:WidgetDuracion"),
            MusicService.FormatearDuracion(track.Duration, track.IsLiveStream), true);

        return builder.Build();
    }

    private DiscordButtonComponent[] ConstruirBotones(bool deshabilitados = false) =>
    [
        new(ButtonStyle.Primary, CustomIdPause, "⏯️", deshabilitados),
        new(ButtonStyle.Secondary, CustomIdSkip, "⏭️", deshabilitados),
        new(ButtonStyle.Secondary, CustomIdCola, "📋", deshabilitados),
        new(ButtonStyle.Danger, CustomIdStop, "⏹️", deshabilitados),
    ];
}