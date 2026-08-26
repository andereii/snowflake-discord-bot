using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Microsoft.EntityFrameworkCore;
using Snowflake.Bot.Data;
using Snowflake.Bot.Data.Entities;
using Snowflake.Bot.Services;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Comandos de moderación. Todas las acciones quedan documentadas en la base
/// de datos (historial de incidentes) y se anuncian en el canal de logs.
/// Los textos de las respuestas están en messages.json (sección "Moderacion").
/// </summary>
public sealed class ModerationModule : SnowflakeModuleBase
{
    // Discord no permite aislamientos (timeouts) de más de 28 días.
    private static readonly TimeSpan MaxAislamiento = TimeSpan.FromDays(28);

    private readonly IDbContextFactory<BotDbContext> _dbFactory;
    private readonly ModerationLogService _log;
    private readonly MessagesService _msg;

    public ModerationModule(
        IDbContextFactory<BotDbContext> dbFactory,
        ModerationLogService log,
        MessagesService msg)
    {
        _dbFactory = dbFactory;
        _log = log;
        _msg = msg;
    }

    [SlashCommand("kick", "Kick a user from the server")]
    [NameLocalization(Localization.Spanish, "expulsar")]
    [NameLocalization(Localization.Portuguese, "expulsar")]
    [DescriptionLocalization(Localization.Spanish, "Expulsa a un usuario del servidor")]
    [DescriptionLocalization(Localization.Portuguese, "Expulsa um usuário do servidor")]
    [SlashRequirePermissions(Permissions.KickMembers)]
    [SlashRequireBotPermissions(Permissions.KickMembers)]
    public async Task ExpulsarAsync(
        InteractionContext ctx,
        [Option("user", "User to kick")]
        [NameLocalization(Localization.Spanish, "usuario")]
        [NameLocalization(Localization.Portuguese, "usuário")]
        [DescriptionLocalization(Localization.Spanish, "Usuario a expulsar")]
        [DescriptionLocalization(Localization.Portuguese, "Usuário a expulsar")] DiscordUser usuario,
        [Option("reason", "Reason for the kick")]
        [NameLocalization(Localization.Spanish, "motivo")]
        [NameLocalization(Localization.Portuguese, "motivo")]
        [DescriptionLocalization(Localization.Spanish, "Motivo de la expulsión")]
        [DescriptionLocalization(Localization.Portuguese, "Motivo da expulsão")] string? motivo = null)
    {
        motivo ??= _msg.Get(ctx.Guild.Id, "Moderacion:MotivoPorDefecto");

        var miembro = await ValidarObjetivoAsync(ctx, usuario);
        if (miembro is null) return;

        await IntentarAvisoPrivadoAsync(miembro, ctx.Guild.Name,
            _msg.Get(ctx.Guild.Id, "Moderacion:Dm:Acciones:Expulsion"), motivo);
        await miembro.RemoveAsync(motivo);

        var incidente = await _log.RegistrarAsync(ctx.Guild.Id, usuario, ctx.User, IncidentType.Expulsion, motivo);
        await _log.AnunciarAsync(ctx.Guild, incidente);
        await ResponderExitoAsync(ctx,
            _msg.Get(ctx.Guild.Id, "Moderacion:Exito:Expulsion", ("usuario", usuario.Username)), incidente);
    }

    [SlashCommand("ban", "Ban a user from the server")]
    [NameLocalization(Localization.Spanish, "vetar")]
    [NameLocalization(Localization.Portuguese, "banir")]
    [DescriptionLocalization(Localization.Spanish, "Veta (banea) a un usuario del servidor")]
    [DescriptionLocalization(Localization.Portuguese, "Bane um usuário do servidor")]
    [SlashRequirePermissions(Permissions.BanMembers)]
    [SlashRequireBotPermissions(Permissions.BanMembers)]
    public async Task VetarAsync(
        InteractionContext ctx,
        [Option("user", "User to ban")]
        [NameLocalization(Localization.Spanish, "usuario")]
        [NameLocalization(Localization.Portuguese, "usuário")]
        [DescriptionLocalization(Localization.Spanish, "Usuario a vetar")]
        [DescriptionLocalization(Localization.Portuguese, "Usuário a banir")] DiscordUser usuario,
        [Option("reason", "Reason for the ban")]
        [NameLocalization(Localization.Spanish, "motivo")]
        [NameLocalization(Localization.Portuguese, "motivo")]
        [DescriptionLocalization(Localization.Spanish, "Motivo del veto")]
        [DescriptionLocalization(Localization.Portuguese, "Motivo do banimento")] string? motivo = null,
        [Option("delete_days", "Days of messages to delete (0-7)")]
        [NameLocalization(Localization.Spanish, "borrar_dias")]
        [NameLocalization(Localization.Portuguese, "excluir_dias")]
        [DescriptionLocalization(Localization.Spanish, "Días de mensajes a borrar (0-7)")]
        [DescriptionLocalization(Localization.Portuguese, "Dias de mensagens a excluir (0-7)")] long borrarDias = 0)
    {
        motivo ??= _msg.Get(ctx.Guild.Id, "Moderacion:MotivoPorDefecto");

        if (usuario.Id == ctx.User.Id)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Moderacion:Errores:MismoUsuario"));
            return;
        }
        if (usuario.Id == ctx.Client.CurrentUser.Id)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Moderacion:Errores:AlBot"));
            return;
        }

        // El veto funciona aunque el usuario ya no esté en el servidor.
        var miembro = await BuscarMiembroAsync(ctx, usuario.Id);
        if (miembro is not null)
        {
            if (!await ValidarJerarquiaAsync(ctx, miembro)) return;
            await IntentarAvisoPrivadoAsync(miembro, ctx.Guild.Name,
                _msg.Get(ctx.Guild.Id, "Moderacion:Dm:Acciones:Veto"), motivo);
        }

        await ctx.Guild.BanMemberAsync(usuario.Id, (int)Math.Clamp(borrarDias, 0, 7), motivo);

        var incidente = await _log.RegistrarAsync(ctx.Guild.Id, usuario, ctx.User, IncidentType.Veto, motivo);
        await _log.AnunciarAsync(ctx.Guild, incidente);
        await ResponderExitoAsync(ctx,
            _msg.Get(ctx.Guild.Id, "Moderacion:Exito:Veto", ("usuario", usuario.Username)), incidente);
    }

    [SlashCommand("timeout", "Time out (mute) a user")]
    [NameLocalization(Localization.Spanish, "aislar")]
    [NameLocalization(Localization.Portuguese, "silenciar")]
    [DescriptionLocalization(Localization.Spanish, "Aísla (timeout) a un usuario durante un tiempo")]
    [DescriptionLocalization(Localization.Portuguese, "Silencia um usuário por um tempo")]
    [SlashRequirePermissions(Permissions.ModerateMembers)]
    [SlashRequireBotPermissions(Permissions.ModerateMembers)]
    public async Task AislarAsync(
        InteractionContext ctx,
        [Option("user", "User to time out")]
        [NameLocalization(Localization.Spanish, "usuario")]
        [NameLocalization(Localization.Portuguese, "usuário")]
        [DescriptionLocalization(Localization.Spanish, "Usuario a aislar")]
        [DescriptionLocalization(Localization.Portuguese, "Usuário a silenciar")] DiscordUser usuario,
        [Option("duration", "Duration: 30s, 10m, 2h, 7d (max 28 days)")]
        [NameLocalization(Localization.Spanish, "duracion")]
        [NameLocalization(Localization.Portuguese, "duração")]
        [DescriptionLocalization(Localization.Spanish, "Duración: 30s, 10m, 2h, 7d (máx. 28 días)")]
        [DescriptionLocalization(Localization.Portuguese, "Duração: 30s, 10m, 2h, 7d (máx. 28 dias)")] string duracion,
        [Option("reason", "Reason for the timeout")]
        [NameLocalization(Localization.Spanish, "motivo")]
        [NameLocalization(Localization.Portuguese, "motivo")]
        [DescriptionLocalization(Localization.Spanish, "Motivo del aislamiento")]
        [DescriptionLocalization(Localization.Portuguese, "Motivo do silêncio")] string? motivo = null)
    {
        motivo ??= _msg.Get(ctx.Guild.Id, "Moderacion:MotivoPorDefecto");

        if (!DurationParser.TryParse(duracion, out var tiempo) || tiempo <= TimeSpan.Zero)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Moderacion:Errores:DuracionInvalida"));
            return;
        }
        if (tiempo > MaxAislamiento)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Moderacion:Errores:DuracionMaxima"));
            return;
        }

        var miembro = await ValidarObjetivoAsync(ctx, usuario);
        if (miembro is null) return;

        await IntentarAvisoPrivadoAsync(miembro, ctx.Guild.Name,
            _msg.Get(ctx.Guild.Id, "Moderacion:Dm:Acciones:Aislamiento", ("duracion", DurationParser.Format(tiempo, _msg.Locale(ctx.Guild.Id)))), motivo);
        await miembro.TimeoutAsync(DateTimeOffset.UtcNow + tiempo, motivo);

        var incidente = await _log.RegistrarAsync(ctx.Guild.Id, usuario, ctx.User, IncidentType.Aislamiento, motivo, tiempo);
        await _log.AnunciarAsync(ctx.Guild, incidente);
        await ResponderExitoAsync(ctx,
            _msg.Get(ctx.Guild.Id, "Moderacion:Exito:Aislamiento",
                ("usuario", usuario.Username),
                ("duracion", DurationParser.Format(tiempo, _msg.Locale(ctx.Guild.Id)))),
            incidente);
    }

    [SlashCommand("untimeout", "Remove a user's timeout")]
    [NameLocalization(Localization.Spanish, "desaislar")]
    [NameLocalization(Localization.Portuguese, "dessilenciar")]
    [DescriptionLocalization(Localization.Spanish, "Quita el aislamiento (timeout) a un usuario")]
    [DescriptionLocalization(Localization.Portuguese, "Remove o silêncio de um usuário")]
    [SlashRequirePermissions(Permissions.ModerateMembers)]
    [SlashRequireBotPermissions(Permissions.ModerateMembers)]
    public async Task DesaislarAsync(
        InteractionContext ctx,
        [Option("user", "User to remove the timeout from")]
        [NameLocalization(Localization.Spanish, "usuario")]
        [NameLocalization(Localization.Portuguese, "usuário")]
        [DescriptionLocalization(Localization.Spanish, "Usuario a desaislar")]
        [DescriptionLocalization(Localization.Portuguese, "Usuário a dessilenciar")] DiscordUser usuario,
        [Option("reason", "Reason")]
        [NameLocalization(Localization.Spanish, "motivo")]
        [NameLocalization(Localization.Portuguese, "motivo")]
        [DescriptionLocalization(Localization.Spanish, "Motivo")]
        [DescriptionLocalization(Localization.Portuguese, "Motivo")] string? motivo = null)
    {
        motivo ??= _msg.Get(ctx.Guild.Id, "Moderacion:MotivoPorDefecto");

        var miembro = await ValidarObjetivoAsync(ctx, usuario);
        if (miembro is null) return;

        await miembro.TimeoutAsync(null, motivo);

        var incidente = await _log.RegistrarAsync(ctx.Guild.Id, usuario, ctx.User, IncidentType.FinAislamiento, motivo);
        await _log.AnunciarAsync(ctx.Guild, incidente);
        await ResponderExitoAsync(ctx,
            _msg.Get(ctx.Guild.Id, "Moderacion:Exito:FinAislamiento", ("usuario", usuario.Username)), incidente);
    }

    [SlashCommand("warn", "Record a warning for a user")]
    [NameLocalization(Localization.Spanish, "advertir")]
    [NameLocalization(Localization.Portuguese, "advertir")]
    [DescriptionLocalization(Localization.Spanish, "Registra una advertencia a un usuario")]
    [DescriptionLocalization(Localization.Portuguese, "Registra uma advertência para um usuário")]
    [SlashRequirePermissions(Permissions.ModerateMembers)]
    public async Task AdvertirAsync(
        InteractionContext ctx,
        [Option("user", "User to warn")]
        [NameLocalization(Localization.Spanish, "usuario")]
        [NameLocalization(Localization.Portuguese, "usuário")]
        [DescriptionLocalization(Localization.Spanish, "Usuario a advertir")]
        [DescriptionLocalization(Localization.Portuguese, "Usuário a advertir")] DiscordUser usuario,
        [Option("reason", "Reason for the warning")]
        [NameLocalization(Localization.Spanish, "motivo")]
        [NameLocalization(Localization.Portuguese, "motivo")]
        [DescriptionLocalization(Localization.Spanish, "Motivo de la advertencia")]
        [DescriptionLocalization(Localization.Portuguese, "Motivo da advertência")] string motivo)
    {
        var miembro = await ValidarObjetivoAsync(ctx, usuario);
        if (miembro is null) return;

        await IntentarAvisoPrivadoAsync(miembro, ctx.Guild.Name,
            _msg.Get(ctx.Guild.Id, "Moderacion:Dm:Acciones:Advertencia"), motivo);

        var incidente = await _log.RegistrarAsync(ctx.Guild.Id, usuario, ctx.User, IncidentType.Advertencia, motivo);
        await _log.AnunciarAsync(ctx.Guild, incidente);
        await ResponderExitoAsync(ctx,
            _msg.Get(ctx.Guild.Id, "Moderacion:Exito:Advertencia", ("usuario", usuario.Username)), incidente);
    }

    [SlashCommand("history", "Show a user's incidents (or the server's latest)")]
    [NameLocalization(Localization.Spanish, "historial")]
    [NameLocalization(Localization.Portuguese, "historico")]
    [DescriptionLocalization(Localization.Spanish, "Muestra los incidentes de un usuario (o los últimos del servidor)")]
    [DescriptionLocalization(Localization.Portuguese, "Mostra os incidentes de um usuário (ou os últimos do servidor)")]
    [SlashRequirePermissions(Permissions.ModerateMembers)]
    public async Task HistorialAsync(
        InteractionContext ctx,
        [Option("user", "User to look up (empty = server's latest)")]
        [NameLocalization(Localization.Spanish, "usuario")]
        [NameLocalization(Localization.Portuguese, "usuário")]
        [DescriptionLocalization(Localization.Spanish, "Usuario a consultar (vacío = últimos del servidor)")]
        [DescriptionLocalization(Localization.Portuguese, "Usuário a consultar (vazio = últimos do servidor)")] DiscordUser? usuario = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var consulta = db.Incidents.Where(i => i.GuildId == ctx.Guild.Id);
        if (usuario is not null)
            consulta = consulta.Where(i => i.TargetUserId == usuario.Id);

        var ultimos = await consulta.OrderByDescending(i => i.Id).Take(10).ToListAsync();

        var embed = new DiscordEmbedBuilder()
            .WithTitle(usuario is null
                ? _msg.Get(ctx.Guild.Id, "Moderacion:Historial:TituloServidor")
                : _msg.Get(ctx.Guild.Id, "Moderacion:Historial:TituloUsuario", ("usuario", usuario.Username)))
            .WithColor(DiscordColor.Blurple);

        if (ultimos.Count == 0)
        {
            embed.WithDescription(_msg.Get(ctx.Guild.Id, "Moderacion:Historial:Vacio"));
        }
        else
        {
            foreach (var i in ultimos)
            {
                var duracion = i.Duration is { } d ? $" · {DurationParser.Format(d, _msg.Locale(ctx.Guild.Id))}" : "";
                var cabecera = _msg.Get(ctx.Guild.Id, "Moderacion:Historial:CabeceraCaso",
                    ("caso", i.Id),
                    ("tipo", _msg.Get(ctx.Guild.Id, $"Moderacion:Tipos:{i.Type}")),
                    ("duracion", duracion),
                    ("fecha", $"<t:{i.CreatedAt.ToUnixTimeSeconds()}:d>"));
                var linea = _msg.Get(ctx.Guild.Id, "Moderacion:Historial:Linea",
                    ("usuario", $"<@{i.TargetUserId}>"),
                    ("moderador", $"<@{i.ModeratorId}>"),
                    ("motivo", i.Reason));
                embed.AddField(cabecera, linea);
            }
        }

        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(embed).AsEphemeral());
    }

    [SlashCommand("softban", "Ban and immediately unban to delete messages")]
    [NameLocalization(Localization.Spanish, "softban")]
    [NameLocalization(Localization.Portuguese, "softban")]
    [DescriptionLocalization(Localization.Spanish, "Banea y desbanea al instante para borrar mensajes")]
    [DescriptionLocalization(Localization.Portuguese, "Bane e desbane instantaneamente para apagar mensagens")]
    [SlashRequirePermissions(Permissions.BanMembers)]
    [SlashRequireBotPermissions(Permissions.BanMembers)]
    public async Task SoftbanAsync(
        InteractionContext ctx,
        [Option("user", "User to softban")]
        [NameLocalization(Localization.Spanish, "usuario")]
        [NameLocalization(Localization.Portuguese, "usuário")]
        [DescriptionLocalization(Localization.Spanish, "Usuario a softbanear")]
        [DescriptionLocalization(Localization.Portuguese, "Usuário a softbanir")] DiscordUser usuario,
        [Option("reason", "Reason for the softban")]
        [NameLocalization(Localization.Spanish, "motivo")]
        [NameLocalization(Localization.Portuguese, "motivo")]
        [DescriptionLocalization(Localization.Spanish, "Motivo del softban")]
        [DescriptionLocalization(Localization.Portuguese, "Motivo do softban")] string? motivo = null)
    {
        motivo ??= _msg.Get(ctx.Guild.Id, "Moderacion:MotivoPorDefecto");

        if (usuario.Id == ctx.User.Id)
        { await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Moderacion:Errores:MismoUsuario")); return; }
        if (usuario.Id == ctx.Client.CurrentUser.Id)
        { await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Moderacion:Errores:AlBot")); return; }

        var miembro = await BuscarMiembroAsync(ctx, usuario.Id);
        if (miembro is not null)
        {
            if (!await ValidarJerarquiaAsync(ctx, miembro)) return;
            await IntentarAvisoPrivadoAsync(miembro, ctx.Guild.Name,
                _msg.Get(ctx.Guild.Id, "Moderacion:Dm:Acciones:Softban"), motivo);
        }

        await ctx.Guild.BanMemberAsync(usuario.Id, 7, motivo);
        await ctx.Guild.UnbanMemberAsync(usuario.Id, "Softban: unban automático");

        var incidente = await _log.RegistrarAsync(ctx.Guild.Id, usuario, ctx.User, IncidentType.Softban, motivo);
        await _log.AnunciarAsync(ctx.Guild, incidente);
        await ResponderExitoAsync(ctx,
            _msg.Get(ctx.Guild.Id, "Moderacion:Exito:Softban", ("usuario", usuario.Username)), incidente);
    }

    [SlashCommand("mute", "Mute a user (timeout)")]
    [NameLocalization(Localization.Spanish, "mute")]
    [NameLocalization(Localization.Portuguese, "mute")]
    [DescriptionLocalization(Localization.Spanish, "Silencia a un usuario (timeout)")]
    [DescriptionLocalization(Localization.Portuguese, "Silencia um usuário (timeout)")]
    [SlashRequirePermissions(Permissions.ModerateMembers)]
    [SlashRequireBotPermissions(Permissions.ModerateMembers)]
    public async Task MuteAsync(
        InteractionContext ctx,
        [Option("user", "User to mute")]
        [NameLocalization(Localization.Spanish, "usuario")]
        [NameLocalization(Localization.Portuguese, "usuário")]
        [DescriptionLocalization(Localization.Spanish, "Usuario a silenciar")]
        [DescriptionLocalization(Localization.Portuguese, "Usuário a silenciar")] DiscordUser usuario,
        [Option("duration", "Duration: 30s, 10m, 2h, 7d (max 28 days)")]
        [NameLocalization(Localization.Spanish, "duracion")]
        [NameLocalization(Localization.Portuguese, "duracao")]
        [DescriptionLocalization(Localization.Spanish, "Duración: 30s, 10m, 2h, 7d (máx. 28 días)")]
        [DescriptionLocalization(Localization.Portuguese, "Duração: 30s, 10m, 2h, 7d (máx. 28 dias)")] string duracion,
        [Option("reason", "Reason")]
        [NameLocalization(Localization.Spanish, "motivo")]
        [NameLocalization(Localization.Portuguese, "motivo")]
        [DescriptionLocalization(Localization.Spanish, "Motivo")]
        [DescriptionLocalization(Localization.Portuguese, "Motivo")] string? motivo = null)
    {
        motivo ??= _msg.Get(ctx.Guild.Id, "Moderacion:MotivoPorDefecto");

        if (!DurationParser.TryParse(duracion, out var tiempo) || tiempo <= TimeSpan.Zero)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Moderacion:Errores:DuracionInvalida"));
            return;
        }
        if (tiempo > MaxAislamiento)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Moderacion:Errores:DuracionMaxima"));
            return;
        }

        var miembro = await ValidarObjetivoAsync(ctx, usuario);
        if (miembro is null) return;

        await IntentarAvisoPrivadoAsync(miembro, ctx.Guild.Name,
            _msg.Get(ctx.Guild.Id, "Moderacion:Dm:Acciones:Silencio",
                ("duracion", DurationParser.Format(tiempo, _msg.Locale(ctx.Guild.Id)))), motivo);
        await miembro.TimeoutAsync(DateTimeOffset.UtcNow + tiempo, motivo);

        var incidente = await _log.RegistrarAsync(ctx.Guild.Id, usuario, ctx.User, IncidentType.Silencio, motivo, tiempo);
        await _log.AnunciarAsync(ctx.Guild, incidente);
        await ResponderExitoAsync(ctx,
            _msg.Get(ctx.Guild.Id, "Moderacion:Exito:Silencio",
                ("usuario", usuario.Username),
                ("duracion", DurationParser.Format(tiempo, _msg.Locale(ctx.Guild.Id)))),
            incidente);
    }

    [SlashCommand("hardmute", "Strip roles and revoke send/speak in all channels")]
    [NameLocalization(Localization.Spanish, "hardmute")]
    [NameLocalization(Localization.Portuguese, "hardmute")]
    [DescriptionLocalization(Localization.Spanish, "Quita roles y revoca permisos de enviar/hablar en todos los canales")]
    [DescriptionLocalization(Localization.Portuguese, "Remove cargos e revoga permissões de enviar/falar em todos os canais")]
    [SlashRequirePermissions(Permissions.ManageRoles)]
    [SlashRequireBotPermissions(Permissions.ManageRoles | Permissions.ManageChannels)]
    public async Task HardmuteAsync(
        InteractionContext ctx,
        [Option("user", "User to hardmute")]
        [NameLocalization(Localization.Spanish, "usuario")]
        [NameLocalization(Localization.Portuguese, "usuário")]
        [DescriptionLocalization(Localization.Spanish, "Usuario a hardmutear")]
        [DescriptionLocalization(Localization.Portuguese, "Usuário a hardmutar")] DiscordUser usuario,
        [Option("reason", "Reason")]
        [NameLocalization(Localization.Spanish, "motivo")]
        [NameLocalization(Localization.Portuguese, "motivo")]
        [DescriptionLocalization(Localization.Spanish, "Motivo del hardmute")]
        [DescriptionLocalization(Localization.Portuguese, "Motivo do hardmute")] string? motivo = null)
    {
        motivo ??= _msg.Get(ctx.Guild.Id, "Moderacion:MotivoPorDefecto");

        var miembro = await ValidarObjetivoAsync(ctx, usuario);
        if (miembro is null) return;

        await ctx.DeferAsync();

        // 1. Guardar y quitar roles
        var rolesQuitar = miembro.Roles
            .Where(r => r.Id != ctx.Guild.EveryoneRole.Id && !r.IsManaged
                        && r.Position < ctx.Guild.CurrentMember.Hierarchy)
            .ToList();

        if (rolesQuitar.Count > 0)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var backup = await db.HardmuteBackups
                .FirstOrDefaultAsync(h => h.GuildId == ctx.Guild.Id && h.UserId == usuario.Id);

            var idsTexto = string.Join(",", rolesQuitar.Select(r => r.Id));
            if (backup is null)
            {
                db.HardmuteBackups.Add(new HardmuteBackup
                {
                    GuildId = ctx.Guild.Id,
                    UserId = usuario.Id,
                    RoleIds = idsTexto
                });
            }
            else
            {
                backup.RoleIds = idsTexto;
                backup.CreatedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync();

            foreach (var rol in rolesQuitar)
            {
                try { await miembro.RevokeRoleAsync(rol, $"Hardmute por {ctx.User.Username}"); }
                catch { /* Rol no removible */ }
            }
        }

        // 2. Denegar permisos en canales
        foreach (var canal in ctx.Guild.Channels.Values)
        {
            if (canal.Type is not (ChannelType.Text or ChannelType.Voice
                or ChannelType.PublicThread or ChannelType.PrivateThread
                or ChannelType.News or ChannelType.Stage or ChannelType.GuildForum))
                continue;

            try
            {
                await canal.AddOverwriteAsync(miembro,
                    deny: Permissions.SendMessages | Permissions.Speak | Permissions.SendMessagesInThreads,
                    reason: $"Hardmute por {ctx.User.Username}: {motivo}");
            }
            catch { /* Sin acceso */ }
        }

        await IntentarAvisoPrivadoAsync(miembro, ctx.Guild.Name,
            _msg.Get(ctx.Guild.Id, "Moderacion:Dm:Acciones:Hardmute"), motivo);

        var incidente = await _log.RegistrarAsync(ctx.Guild.Id, usuario, ctx.User, IncidentType.Hardmute, motivo);
        await _log.AnunciarAsync(ctx.Guild, incidente);

        var embed = new DiscordEmbedBuilder()
            .WithDescription(_msg.Get(ctx.Guild.Id, "Moderacion:Exito:Formato",
                ("texto", _msg.Get(ctx.Guild.Id, "Moderacion:Exito:Hardmute", ("usuario", usuario.Username))),
                ("motivo", incidente.Reason)))
            .WithFooter(_msg.Get(ctx.Guild.Id, "Moderacion:Caso", ("caso", incidente.Id)))
            .WithColor(DiscordColor.Green);
        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
    }

    [SlashCommand("unhardmute", "Restore roles and permissions after a hardmute")]
    [NameLocalization(Localization.Spanish, "unhardmute")]
    [NameLocalization(Localization.Portuguese, "unhardmute")]
    [DescriptionLocalization(Localization.Spanish, "Restaura roles y permisos tras un hardmute")]
    [DescriptionLocalization(Localization.Portuguese, "Restaura cargos e permissões após um hardmute")]
    [SlashRequirePermissions(Permissions.ManageRoles)]
    [SlashRequireBotPermissions(Permissions.ManageRoles | Permissions.ManageChannels)]
    public async Task UnhardmuteAsync(
        InteractionContext ctx,
        [Option("user", "User to unhardmute")]
        [NameLocalization(Localization.Spanish, "usuario")]
        [NameLocalization(Localization.Portuguese, "usuário")]
        [DescriptionLocalization(Localization.Spanish, "Usuario a deshardmutear")]
        [DescriptionLocalization(Localization.Portuguese, "Usuário a deshardmutar")] DiscordUser usuario,
        [Option("reason", "Reason")]
        [NameLocalization(Localization.Spanish, "motivo")]
        [NameLocalization(Localization.Portuguese, "motivo")]
        [DescriptionLocalization(Localization.Spanish, "Motivo")]
        [DescriptionLocalization(Localization.Portuguese, "Motivo")] string? motivo = null)
    {
        motivo ??= _msg.Get(ctx.Guild.Id, "Moderacion:MotivoPorDefecto");

        var miembro = await ValidarObjetivoAsync(ctx, usuario);
        if (miembro is null) return;

        await ctx.DeferAsync();

        // 1. Restaurar roles desde backup
        await using var db = await _dbFactory.CreateDbContextAsync();
        var backup = await db.HardmuteBackups
            .FirstOrDefaultAsync(h => h.GuildId == ctx.Guild.Id && h.UserId == usuario.Id);

        if (backup is not null)
        {
            var roleIds = backup.RoleIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => ulong.TryParse(s, out var id) ? id : 0)
                .Where(id => id != 0);

            foreach (var roleId in roleIds)
            {
                var rol = ctx.Guild.GetRole(roleId);
                if (rol is not null && !rol.IsManaged && rol.Position < ctx.Guild.CurrentMember.Hierarchy)
                {
                    try { await miembro.GrantRoleAsync(rol, $"Unhardmute por {ctx.User.Username}"); }
                    catch { }
                }
            }

            db.HardmuteBackups.Remove(backup);
            await db.SaveChangesAsync();
        }

        // 2. Eliminar overrides del miembro en todos los canales
        foreach (var canal in ctx.Guild.Channels.Values)
        {
            var overwrite = canal.PermissionOverwrites?
                .FirstOrDefault(o => o.Id == miembro.Id && o.Type == OverwriteType.Member);
            if (overwrite is not null)
            {
                try { await overwrite.DeleteAsync($"Unhardmute: {motivo}"); }
                catch { }
            }
        }

        var incidente = await _log.RegistrarAsync(ctx.Guild.Id, usuario, ctx.User, IncidentType.FinHardmute, motivo);
        await _log.AnunciarAsync(ctx.Guild, incidente);

        var embed = new DiscordEmbedBuilder()
            .WithDescription(_msg.Get(ctx.Guild.Id, "Moderacion:Exito:Formato",
                ("texto", _msg.Get(ctx.Guild.Id, "Moderacion:Exito:FinHardmute", ("usuario", usuario.Username))),
                ("motivo", incidente.Reason)))
            .WithFooter(_msg.Get(ctx.Guild.Id, "Moderacion:Caso", ("caso", incidente.Id)))
            .WithColor(DiscordColor.Green);
        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
    }

    // ------------------------- Ayudantes internos -------------------------

    private static async Task<DiscordMember?> BuscarMiembroAsync(InteractionContext ctx, ulong userId)
    {
        try
        {
            return await ctx.Guild.GetMemberAsync(userId);
        }
        catch
        {
            return null; // No está en el servidor.
        }
    }

    /// <summary>Comprueba dueño del servidor y jerarquía de roles respecto al bot.</summary>
    private async Task<bool> ValidarJerarquiaAsync(InteractionContext ctx, DiscordMember miembro)
    {
        if (miembro.IsOwner)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Moderacion:Errores:EsOwner"));
            return false;
        }
        if (miembro.Hierarchy >= ctx.Guild.CurrentMember.Hierarchy)
        {
            await ResponderErrorAsync(ctx,
                _msg.Get(ctx.Guild.Id, "Moderacion:Errores:Jerarquia", ("usuario", miembro.Username)));
            return false;
        }
        return true;
    }

    /// <summary>Valida que el objetivo sea un miembro del servidor sobre el que se pueda actuar.</summary>
    private async Task<DiscordMember?> ValidarObjetivoAsync(InteractionContext ctx, DiscordUser usuario)
    {
        if (usuario.Id == ctx.User.Id)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Moderacion:Errores:MismoUsuario"));
            return null;
        }
        if (usuario.Id == ctx.Client.CurrentUser.Id)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Moderacion:Errores:AlBot"));
            return null;
        }

        var miembro = await BuscarMiembroAsync(ctx, usuario.Id);
        if (miembro is null)
        {
            await ResponderErrorAsync(ctx,
                _msg.Get(ctx.Guild.Id, "Moderacion:Errores:NoEnServidor", ("usuario", usuario.Username)));
            return null;
        }

        return await ValidarJerarquiaAsync(ctx, miembro) ? miembro : null;
    }

    /// <summary>Intenta avisar al usuario por MD antes de la acción (si tiene los MD abiertos).</summary>
    private Task IntentarAvisoPrivadoAsync(
        DiscordMember miembro, string servidor, string accion, string motivo)
        => _log.AvisarPrivadoAsync(miembro, servidor, accion, motivo);

    private async Task ResponderExitoAsync(InteractionContext ctx, string texto, Incident incidente)
    {
        var embed = new DiscordEmbedBuilder()
            .WithDescription(_msg.Get(ctx.Guild.Id, "Moderacion:Exito:Formato",
                ("texto", texto), ("motivo", incidente.Reason)))
            .WithFooter(_msg.Get(ctx.Guild.Id, "Moderacion:Caso", ("caso", incidente.Id)))
            .WithColor(DiscordColor.Green);

        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(embed));
    }
}
