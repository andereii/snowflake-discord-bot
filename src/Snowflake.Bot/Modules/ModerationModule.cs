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
