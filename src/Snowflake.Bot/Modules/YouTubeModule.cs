using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Snowflake.Bot.Data;
using Snowflake.Bot.Data.Entities;
using Snowflake.Bot.Services;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Notificaciones de YouTube: el bot vigila el feed RSS público de un canal y
/// avisa en un canal de Discord cuando se sube un vídeo nuevo.
/// </summary>
[SlashCommandGroup("youtube", "Suscripciones de notificaciones de YouTube")]
public sealed class YouTubeModule : ApplicationCommandModule
{
    private readonly IDbContextFactory<BotDbContext> _dbFactory;
    private readonly YouTubeNotifyService _yt;
    private readonly MessagesService _msg;
    private readonly ILogger<YouTubeModule> _logger;

    public YouTubeModule(
        IDbContextFactory<BotDbContext> dbFactory,
        YouTubeNotifyService yt,
        MessagesService msg,
        ILogger<YouTubeModule> logger)
    {
        _dbFactory = dbFactory;
        _yt = yt;
        _msg = msg;
        _logger = logger;
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

        var resuelto = await YouTubeNotifyService.ResolverCanalAsync(canal, _logger);
        if (resuelto is null)
        {
            await SafeEditAsync(ctx, _msg.Get("YouTube:ErrorResolver"));
            return;
        }
        var (channelId, channelName) = resuelto.Value;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var existente = await db.YouTubeSubscriptions.FindAsync(ctx.Guild.Id);
        var reemplazado = existente is not null;

        if (existente is null)
        {
            existente = new YouTubeSubscription
            {
                GuildId = ctx.Guild.Id,
                YTChannelId = channelId,
                YTChannelName = channelName,
                NotifyChannelId = notificar.Id,
                NotifyRoleId = rol?.Id,
                LastVideoId = null, // backfill en el primer ciclo
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.YouTubeSubscriptions.Add(existente);
        }
        else
        {
            existente.YTChannelId = channelId;
            existente.YTChannelName = channelName;
            existente.NotifyChannelId = notificar.Id;
            existente.NotifyRoleId = rol?.Id;
            existente.LastVideoId = null; // backfill de nuevo
        }

        await db.SaveChangesAsync();

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
        await using var db = await _dbFactory.CreateDbContextAsync();
        var sub = await db.YouTubeSubscriptions.FindAsync(ctx.Guild.Id);
        if (sub is null)
        {
            await ResponderAsync(ctx, _msg.Get("YouTube:NoSuscrito"), ephemeral: true);
            return;
        }

        db.YouTubeSubscriptions.Remove(sub);
        await db.SaveChangesAsync();

        await ResponderAsync(ctx, _msg.Get("YouTube:Dejado"));
    }

    // ------------------------- Ver -------------------------

    [SlashCommand("ver", "Muestra la suscripción de YouTube del servidor")]
    public async Task VerAsync(InteractionContext ctx)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var sub = await db.YouTubeSubscriptions.FindAsync(ctx.Guild.Id);
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
        await using var db = await _dbFactory.CreateDbContextAsync();
        var sub = await db.YouTubeSubscriptions.FindAsync(ctx.Guild.Id);
        if (sub is null)
        {
            await ResponderAsync(ctx, _msg.Get("YouTube:NoSuscrito"), ephemeral: true);
            return;
        }

        if (rol is null)
        {
            sub.NotifyRoleId = null;
            await db.SaveChangesAsync();
            await ResponderAsync(ctx, _msg.Get("YouTube:RolQuitado"));
            return;
        }

        sub.NotifyRoleId = rol.Id;
        await db.SaveChangesAsync();
        await ResponderAsync(ctx, _msg.Get("YouTube:RolActualizado", ("rol", rol.Mention)));
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

        await using var db = await _dbFactory.CreateDbContextAsync();
        var sub = await db.YouTubeSubscriptions.FindAsync(ctx.Guild.Id);
        if (sub is null)
        {
            await ResponderAsync(ctx, _msg.Get("YouTube:NoSuscrito"), ephemeral: true);
            return;
        }

        sub.NotifyChannelId = canal.Id;
        await db.SaveChangesAsync();

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
        await using var db = await _dbFactory.CreateDbContextAsync();
        var sub = await db.YouTubeSubscriptions.FindAsync(ctx.Guild.Id);
        if (sub is null)
        {
            await ResponderAsync(ctx, _msg.Get("YouTube:NoSuscrito"), ephemeral: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(mensaje))
        {
            sub.CustomMessage = null;
            await db.SaveChangesAsync();
            await ResponderAsync(ctx, _msg.Get("YouTube:MensajeBorrado"));
            return;
        }

        sub.CustomMessage = mensaje;
        await db.SaveChangesAsync();

        var vista = _msg.Get("YouTube:VistaPrevia");
        var opciones = _msg.Get("YouTube:OpcionesPlantilla");
        await ResponderAsync(ctx, _msg.Get("YouTube:MensajeGuardado",
            ("vista", vista), ("opciones", opciones)));
    }

    // ------------------------- Ayudantes -------------------------

    private static async Task ResponderAsync(InteractionContext ctx, string contenido, bool ephemeral = false)
    {
        var b = new DiscordInteractionResponseBuilder().WithContent(contenido);
        if (ephemeral) b.AsEphemeral();
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, b);
    }

    private static async Task ResponderAsync(InteractionContext ctx, DiscordEmbedBuilder embed)
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