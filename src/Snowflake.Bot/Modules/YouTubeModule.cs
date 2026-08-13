using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Microsoft.Extensions.Logging;
using Snowflake.Bot.Services;
using Snowflake.Bot.Services.Settings;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Notificaciones de YouTube: el bot vigila el feed RSS público de un canal y
/// avisa en un canal de Discord cuando se sube un vídeo nuevo.
/// </summary>
[SlashCommandGroup("youtube", "Suscripciones de notificaciones de YouTube")]
public sealed class YouTubeModule : SnowflakeModuleBase
{
    private readonly GuildSettingsService _settings;
    private readonly YouTubeNotifyService _yt;
    private readonly MessagesService _msg;

    public YouTubeModule(
        GuildSettingsService settings,
        YouTubeNotifyService yt,
        MessagesService msg)
    {
        _settings = settings;
        _yt = yt;
        _msg = msg;
    }

    // ------------------------- Seguir -------------------------

    [SlashCommand("seguir", "Suscribe al bot a un canal de YouTube y avisa de vídeos nuevos")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task SeguirAsync(
        InteractionContext ctx,
        [Option("canal", "URL del canal o @handle (ej: https://www.youtube.com/@mbeantv)")]
        string canal,
        [Option("notificar", "Canal de Discord donde enviar el aviso")] DiscordChannel notificar,
        [Option("rol", "Rol a mencionar en el aviso (opcional)")] DiscordRole? rol = null)
    {
        if (notificar.Type != ChannelType.Text)
        {
            await ResponderAsync(ctx, _msg.Get("Conteo:CanalDebeSerTexto"), ephemeral: true);
            return;
        }

        await ctx.DeferAsync();

        var resuelto = await _yt.ResolverCanalAsync(canal);
        if (resuelto is null)
        {
            await SafeEditAsync(ctx, _msg.Get("YouTube:ErrorResolver"));
            return;
        }
        var (channelId, channelName) = resuelto.Value;

        var existente = await _settings.GetYouTubeAsync(ctx.Guild.Id);
        var reemplazado = existente is not null;

        await _settings.UpdateYouTubeAsync(ctx.Guild.Id, sub =>
        {
            sub.YTChannelId = channelId;
            sub.YTChannelName = channelName;
            sub.NotifyChannelId = notificar.Id;
            sub.NotifyRoleId = rol?.Id;
            sub.LastVideoId = null; // backfill en el primer ciclo
        });

        var texto = reemplazado
            ? _msg.Get("YouTube:SeguirReemplazado", ("canal", channelName), ("destino", notificar.Mention))
            : _msg.Get("YouTube:SeguirExito", ("canal", channelName), ("destino", notificar.Mention));
        await SafeEditAsync(ctx, texto);
    }

    // ------------------------- Dejar -------------------------

    [SlashCommand("dejar", "Elimina la suscripción de YouTube del servidor")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task DejarAsync(InteractionContext ctx)
    {
        var eliminado = await _settings.DeleteYouTubeAsync(ctx.Guild.Id);
        if (!eliminado)
        {
            await ResponderAsync(ctx, _msg.Get("YouTube:NoSuscrito"), ephemeral: true);
            return;
        }
        await ResponderAsync(ctx, _msg.Get("YouTube:Dejado"));
    }

    // ------------------------- Ver -------------------------

    [SlashCommand("ver", "Muestra la suscripción de YouTube del servidor")]
    public async Task VerAsync(InteractionContext ctx)
    {
        var sub = await _settings.GetYouTubeAsync(ctx.Guild.Id);
        if (sub is null)
        {
            await ResponderAsync(ctx, _msg.Get("YouTube:VerSinSuscrito"), ephemeral: true);
            return;
        }

        var embed = new DiscordEmbedBuilder()
            .WithTitle(_msg.Get("YouTube:VerTitulo"))
            .WithColor(DiscordColor.Red)
            .AddField(_msg.Get("YouTube:VerCanal"), sub.YTChannelName, true)
            .AddField(_msg.Get("YouTube:VerDestino"), $"<#{sub.NotifyChannelId}>", true);

        if (sub.NotifyRoleId is { } rolId)
            embed.AddField(_msg.Get("YouTube:VerRol"), $"<@&{rolId}>", true);
        else
            embed.AddField(_msg.Get("YouTube:VerRol"), _msg.Get("YouTube:VerSinRol"), true);

        embed.AddField(_msg.Get("YouTube:VerPlantilla"),
            string.IsNullOrWhiteSpace(sub.CustomMessage)
                ? _msg.Get("YouTube:VerPorDefecto")
                : $"```{sub.CustomMessage}```");

        await ResponderAsync(ctx, embed);
    }

    // ------------------------- Rol -------------------------

    [SlashCommand("rol", "Cambia el rol a mencionar en las notificaciones (vacío = quitar)")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task RolAsync(
        InteractionContext ctx,
        [Option("rol", "Rol a mencionar (vacío = quitar la mención)")] DiscordRole? rol = null)
    {
        var sub = await _settings.GetYouTubeAsync(ctx.Guild.Id);
        if (sub is null)
        {
            await ResponderAsync(ctx, _msg.Get("YouTube:NoSuscrito"), ephemeral: true);
            return;
        }

        await _settings.UpdateYouTubeAsync(ctx.Guild.Id, s => s.NotifyRoleId = rol?.Id);

        var texto = rol is null
            ? _msg.Get("YouTube:RolQuitado")
            : _msg.Get("YouTube:RolActualizado", ("rol", rol.Mention));
        await ResponderAsync(ctx, texto);
    }

    // ------------------------- Canal -------------------------

    [SlashCommand("canal", "Cambia el canal de Discord donde se notifica")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task CanalAsync(
        InteractionContext ctx,
        [Option("canal", "Canal de texto donde avisar")] DiscordChannel canal)
    {
        if (canal.Type != ChannelType.Text)
        {
            await ResponderAsync(ctx, _msg.Get("Conteo:CanalDebeSerTexto"), ephemeral: true);
            return;
        }

        var sub = await _settings.GetYouTubeAsync(ctx.Guild.Id);
        if (sub is null)
        {
            await ResponderAsync(ctx, _msg.Get("YouTube:NoSuscrito"), ephemeral: true);
            return;
        }

        await _settings.UpdateYouTubeAsync(ctx.Guild.Id, s => s.NotifyChannelId = canal.Id);
        await ResponderAsync(ctx, _msg.Get("YouTube:CanalActualizado", ("canal", canal.Mention)));
    }

    // ------------------------- Mensaje -------------------------

    [SlashCommand("mensaje", "Personaliza el mensaje de notificación (placeholders disponibles)")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task MensajeAsync(
        InteractionContext ctx,
        [Option("mensaje", "Plantilla personalizada. Vacío = restablecer al por defecto.")]
        string? mensaje = null)
    {
        var sub = await _settings.GetYouTubeAsync(ctx.Guild.Id);
        if (sub is null)
        {
            await ResponderAsync(ctx, _msg.Get("YouTube:NoSuscrito"), ephemeral: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(mensaje))
        {
            await _settings.UpdateYouTubeAsync(ctx.Guild.Id, s => s.CustomMessage = null);
            await ResponderAsync(ctx, _msg.Get("YouTube:MensajeBorrado"));
            return;
        }

        await _settings.UpdateYouTubeAsync(ctx.Guild.Id, s => s.CustomMessage = mensaje);

        var vista = _msg.Get("YouTube:VistaPrevia");
        var opciones = _msg.Get("YouTube:OpcionesPlantilla");
        await ResponderAsync(ctx, _msg.Get("YouTube:MensajeGuardado",
            ("vista", vista), ("opciones", opciones)));
    }
}
