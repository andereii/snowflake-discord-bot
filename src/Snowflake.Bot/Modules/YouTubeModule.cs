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
[SlashCommandGroup("youtube", "YouTube notification subscriptions")]
[DescriptionLocalization(Localization.Spanish, "Suscripciones de notificaciones de YouTube")]
[DescriptionLocalization(Localization.Portuguese, "Inscrições de notificações do YouTube")]
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

    [SlashCommand("follow", "Subscribe the bot to a YouTube channel and announce new videos")]
    [NameLocalization(Localization.Spanish, "seguir")]
    [NameLocalization(Localization.Portuguese, "seguir")]
    [DescriptionLocalization(Localization.Spanish, "Suscribe al bot a un canal de YouTube y avisa de vídeos nuevos")]
    [DescriptionLocalization(Localization.Portuguese, "Inscreve o bot em um canal do YouTube e avisa sobre vídeos novos")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task SeguirAsync(
        InteractionContext ctx,
        [Option("channel", "Channel URL or @handle (e.g. https://www.youtube.com/@mbeantv)")]
        [NameLocalization(Localization.Spanish, "canal")]
        [NameLocalization(Localization.Portuguese, "canal")]
        [DescriptionLocalization(Localization.Spanish, "URL del canal o @handle (ej: https://www.youtube.com/@mbeantv)")]
        [DescriptionLocalization(Localization.Portuguese, "URL do canal ou @handle (ex.: https://www.youtube.com/@mbeantv)")]
        string canal,
        [Option("notify", "Discord channel where the announcement is sent")]
        [NameLocalization(Localization.Spanish, "notificar")]
        [NameLocalization(Localization.Portuguese, "notificar")]
        [DescriptionLocalization(Localization.Spanish, "Canal de Discord donde enviar el aviso")]
        [DescriptionLocalization(Localization.Portuguese, "Canal do Discord onde o aviso é enviado")] DiscordChannel notificar,
        [Option("role", "Role to mention in the announcement (optional)")]
        [NameLocalization(Localization.Spanish, "rol")]
        [NameLocalization(Localization.Portuguese, "cargo")]
        [DescriptionLocalization(Localization.Spanish, "Rol a mencionar en el aviso (opcional)")]
        [DescriptionLocalization(Localization.Portuguese, "Cargo a mencionar no aviso (opcional)")] DiscordRole? rol = null)
    {
        if (notificar.Type != ChannelType.Text)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Conteo:CanalDebeSerTexto"), ephemeral: true);
            return;
        }

        await ctx.DeferAsync();

        var resuelto = await _yt.ResolverCanalAsync(canal);
        if (resuelto is null)
        {
            await SafeEditAsync(ctx, _msg.Get(ctx.Guild.Id, "YouTube:ErrorResolver"));
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
            ? _msg.Get(ctx.Guild.Id, "YouTube:SeguirReemplazado", ("canal", channelName), ("destino", notificar.Mention))
            : _msg.Get(ctx.Guild.Id, "YouTube:SeguirExito", ("canal", channelName), ("destino", notificar.Mention));
        await SafeEditAsync(ctx, texto);
    }

    // ------------------------- Dejar -------------------------

    [SlashCommand("unfollow", "Remove the server's YouTube subscription")]
    [NameLocalization(Localization.Spanish, "dejar")]
    [NameLocalization(Localization.Portuguese, "deixar-de-seguir")]
    [DescriptionLocalization(Localization.Spanish, "Elimina la suscripción de YouTube del servidor")]
    [DescriptionLocalization(Localization.Portuguese, "Remove a inscrição do YouTube do servidor")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task DejarAsync(InteractionContext ctx)
    {
        var eliminado = await _settings.DeleteYouTubeAsync(ctx.Guild.Id);
        if (!eliminado)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "YouTube:NoSuscrito"), ephemeral: true);
            return;
        }
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "YouTube:Dejado"));
    }

    // ------------------------- Ver -------------------------

    [SlashCommand("show", "Show the server's YouTube subscription")]
    [NameLocalization(Localization.Spanish, "ver")]
    [NameLocalization(Localization.Portuguese, "ver")]
    [DescriptionLocalization(Localization.Spanish, "Muestra la suscripción de YouTube del servidor")]
    [DescriptionLocalization(Localization.Portuguese, "Mostra a inscrição do YouTube do servidor")]
    public async Task VerAsync(InteractionContext ctx)
    {
        var sub = await _settings.GetYouTubeAsync(ctx.Guild.Id);
        if (sub is null)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "YouTube:VerSinSuscrito"), ephemeral: true);
            return;
        }

        var embed = new DiscordEmbedBuilder()
            .WithTitle(_msg.Get(ctx.Guild.Id, "YouTube:VerTitulo"))
            .WithColor(DiscordColor.Red)
            .AddField(_msg.Get(ctx.Guild.Id, "YouTube:VerCanal"), sub.YTChannelName, true)
            .AddField(_msg.Get(ctx.Guild.Id, "YouTube:VerDestino"), $"<#{sub.NotifyChannelId}>", true);

        if (sub.NotifyRoleId is { } rolId)
            embed.AddField(_msg.Get(ctx.Guild.Id, "YouTube:VerRol"), $"<@&{rolId}>", true);
        else
            embed.AddField(_msg.Get(ctx.Guild.Id, "YouTube:VerRol"), _msg.Get(ctx.Guild.Id, "YouTube:VerSinRol"), true);

        embed.AddField(_msg.Get(ctx.Guild.Id, "YouTube:VerPlantilla"),
            string.IsNullOrWhiteSpace(sub.CustomMessage)
                ? _msg.Get(ctx.Guild.Id, "YouTube:VerPorDefecto")
                : $"```{sub.CustomMessage}```");

        await ResponderAsync(ctx, embed);
    }

    // ------------------------- Rol -------------------------

    [SlashCommand("role", "Change the role mentioned in notifications (empty = remove)")]
    [NameLocalization(Localization.Spanish, "rol")]
    [NameLocalization(Localization.Portuguese, "cargo")]
    [DescriptionLocalization(Localization.Spanish, "Cambia el rol a mencionar en las notificaciones (vacío = quitar)")]
    [DescriptionLocalization(Localization.Portuguese, "Muda o cargo mencionado nas notificações (vazio = remover)")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task RolAsync(
        InteractionContext ctx,
        [Option("role", "Role to mention (empty = remove the mention)")]
        [NameLocalization(Localization.Spanish, "rol")]
        [NameLocalization(Localization.Portuguese, "cargo")]
        [DescriptionLocalization(Localization.Spanish, "Rol a mencionar (vacío = quitar la mención)")]
        [DescriptionLocalization(Localization.Portuguese, "Cargo a mencionar (vazio = remover a menção)")] DiscordRole? rol = null)
    {
        var sub = await _settings.GetYouTubeAsync(ctx.Guild.Id);
        if (sub is null)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "YouTube:NoSuscrito"), ephemeral: true);
            return;
        }

        await _settings.UpdateYouTubeAsync(ctx.Guild.Id, s => s.NotifyRoleId = rol?.Id);

        var texto = rol is null
            ? _msg.Get(ctx.Guild.Id, "YouTube:RolQuitado")
            : _msg.Get(ctx.Guild.Id, "YouTube:RolActualizado", ("rol", rol.Mention));
        await ResponderAsync(ctx, texto);
    }

    // ------------------------- Canal -------------------------

    [SlashCommand("channel", "Change the Discord channel where notifications are sent")]
    [NameLocalization(Localization.Spanish, "canal")]
    [NameLocalization(Localization.Portuguese, "canal")]
    [DescriptionLocalization(Localization.Spanish, "Cambia el canal de Discord donde se notifica")]
    [DescriptionLocalization(Localization.Portuguese, "Muda o canal do Discord onde se notifica")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task CanalAsync(
        InteractionContext ctx,
        [Option("channel", "Text channel to announce in")]
        [NameLocalization(Localization.Spanish, "canal")]
        [NameLocalization(Localization.Portuguese, "canal")]
        [DescriptionLocalization(Localization.Spanish, "Canal de texto donde avisar")]
        [DescriptionLocalization(Localization.Portuguese, "Canal de texto para avisar")] DiscordChannel canal)
    {
        if (canal.Type != ChannelType.Text)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Conteo:CanalDebeSerTexto"), ephemeral: true);
            return;
        }

        var sub = await _settings.GetYouTubeAsync(ctx.Guild.Id);
        if (sub is null)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "YouTube:NoSuscrito"), ephemeral: true);
            return;
        }

        await _settings.UpdateYouTubeAsync(ctx.Guild.Id, s => s.NotifyChannelId = canal.Id);
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "YouTube:CanalActualizado", ("canal", canal.Mention)));
    }

    // ------------------------- Mensaje -------------------------

    [SlashCommand("message", "Customize the notification message (placeholders available)")]
    [NameLocalization(Localization.Spanish, "mensaje")]
    [NameLocalization(Localization.Portuguese, "mensagem")]
    [DescriptionLocalization(Localization.Spanish, "Personaliza el mensaje de notificación (placeholders disponibles)")]
    [DescriptionLocalization(Localization.Portuguese, "Personaliza a mensagem de notificação (placeholders disponíveis)")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task MensajeAsync(
        InteractionContext ctx,
        [Option("message", "Custom template. Empty = reset to default.")]
        [NameLocalization(Localization.Spanish, "mensaje")]
        [NameLocalization(Localization.Portuguese, "mensagem")]
        [DescriptionLocalization(Localization.Spanish, "Plantilla personalizada. Vacío = restablecer al por defecto.")]
        [DescriptionLocalization(Localization.Portuguese, "Modelo personalizado. Vazio = redefinir para a padrão.")]
        string? mensaje = null)
    {
        var sub = await _settings.GetYouTubeAsync(ctx.Guild.Id);
        if (sub is null)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "YouTube:NoSuscrito"), ephemeral: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(mensaje))
        {
            await _settings.UpdateYouTubeAsync(ctx.Guild.Id, s => s.CustomMessage = null);
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "YouTube:MensajeBorrado"));
            return;
        }

        await _settings.UpdateYouTubeAsync(ctx.Guild.Id, s => s.CustomMessage = mensaje);

        var vista = _msg.Get(ctx.Guild.Id, "YouTube:VistaPrevia");
        var opciones = _msg.Get(ctx.Guild.Id, "YouTube:OpcionesPlantilla");
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "YouTube:MensajeGuardado",
            ("vista", vista), ("opciones", opciones)));
    }
}
