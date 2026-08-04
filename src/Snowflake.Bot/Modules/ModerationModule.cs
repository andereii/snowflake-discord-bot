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
public sealed class ModerationModule : ApplicationCommandModule
{
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

    [SlashCommand("expulsar", "Expulsa a un usuario del servidor")]
    [SlashRequirePermissions(Permissions.KickMembers)]
    [SlashRequireBotPermissions(Permissions.KickMembers)]
    public async Task ExpulsarAsync(
        InteractionContext ctx,
        [Option("usuario", "Usuario a expulsar")] DiscordUser usuario,
        [Option("motivo", "Motivo de la expulsión")] string? motivo = null)
    {
        motivo ??= _msg.Get("Moderacion:MotivoPorDefecto");

        var miembro = await ValidarObjetivoAsync(ctx, usuario);
        if (miembro is null) return;

        await IntentarAvisoPrivadoAsync(miembro, ctx.Guild.Name,
            _msg.Get("Moderacion:Dm:Acciones:Expulsion"), motivo);
        await miembro.RemoveAsync(motivo);

        var incidente = await _log.RegistrarAsync(ctx.Guild.Id, usuario, ctx.User, IncidentType.Expulsion, motivo);
        await _log.AnunciarAsync(ctx.Guild, incidente);
        await ResponderExitoAsync(ctx,
            _msg.Get("Moderacion:Exito:Expulsion", ("usuario", usuario.Username)), incidente);
    }

    [SlashCommand("vetar", "Veta (banea) a un usuario del servidor")]
    [SlashRequirePermissions(Permissions.BanMembers)]
    [SlashRequireBotPermissions(Permissions.BanMembers)]
    public async Task VetarAsync(
        InteractionContext ctx,
        [Option("usuario", "Usuario a vetar")] DiscordUser usuario,
        [Option("motivo", "Motivo del veto")] string? motivo = null,
        [Option("borrar_dias", "Días de mensajes a borrar (0-7)")] long borrarDias = 0)
    {
        motivo ??= _msg.Get("Moderacion:MotivoPorDefecto");

        if (usuario.Id == ctx.User.Id)
        {
            await ResponderErrorAsync(ctx, _msg.Get("Moderacion:Errores:MismoUsuario"));
            return;
        }
        if (usuario.Id == ctx.Client.CurrentUser.Id)
        {
            await ResponderErrorAsync(ctx, _msg.Get("Moderacion:Errores:AlBot"));
            return;
        }

        // El veto funciona aunque el usuario ya no esté en el servidor.
        var miembro = await BuscarMiembroAsync(ctx, usuario.Id);
        if (miembro is not null)
        {
            if (!await ValidarJerarquiaAsync(ctx, miembro)) return;
            await IntentarAvisoPrivadoAsync(miembro, ctx.Guild.Name,
                _msg.Get("Moderacion:Dm:Acciones:Veto"), motivo);
        }

        await ctx.Guild.BanMemberAsync(usuario.Id, (int)Math.Clamp(borrarDias, 0, 7), motivo);

        var incidente = await _log.RegistrarAsync(ctx.Guild.Id, usuario, ctx.User, IncidentType.Veto, motivo);
        await _log.AnunciarAsync(ctx.Guild, incidente);
        await ResponderExitoAsync(ctx,
            _msg.Get("Moderacion:Exito:Veto", ("usuario", usuario.Username)), incidente);
    }

    [SlashCommand("aislar", "Aísla (timeout) a un usuario durante un tiempo")]
    [SlashRequirePermissions(Permissions.ModerateMembers)]
    [SlashRequireBotPermissions(Permissions.ModerateMembers)]
    public async Task AislarAsync(
        InteractionContext ctx,
        [Option("usuario", "Usuario a aislar")] DiscordUser usuario,
        [Option("duracion", "Duración: 30s, 10m, 2h, 7d (máx. 28 días)")] string duracion,
        [Option("motivo", "Motivo del aislamiento")] string? motivo = null)
    {
        motivo ??= _msg.Get("Moderacion:MotivoPorDefecto");

        if (!DurationParser.TryParse(duracion, out var tiempo) || tiempo <= TimeSpan.Zero)
        {
            await ResponderErrorAsync(ctx, _msg.Get("Moderacion:Errores:DuracionInvalida"));
            return;
        }
        if (tiempo > MaxAislamiento)
        {
            await ResponderErrorAsync(ctx, _msg.Get("Moderacion:Errores:DuracionMaxima"));
            return;
        }

        var miembro = await ValidarObjetivoAsync(ctx, usuario);
        if (miembro is null) return;

        await IntentarAvisoPrivadoAsync(miembro, ctx.Guild.Name,
            _msg.Get("Moderacion:Dm:Acciones:Aislamiento", ("duracion", DurationParser.Format(tiempo))), motivo);
        await miembro.TimeoutAsync(DateTimeOffset.UtcNow + tiempo, motivo);

        var incidente = await _log.RegistrarAsync(ctx.Guild.Id, usuario, ctx.User, IncidentType.Aislamiento, motivo, tiempo);
        await _log.AnunciarAsync(ctx.Guild, incidente);
        await ResponderExitoAsync(ctx,
            _msg.Get("Moderacion:Exito:Aislamiento",
                ("usuario", usuario.Username),
                ("duracion", DurationParser.Format(tiempo))),
            incidente);
    }

    [SlashCommand("desaislar", "Quita el aislamiento (timeout) a un usuario")]
    [SlashRequirePermissions(Permissions.ModerateMembers)]
    [SlashRequireBotPermissions(Permissions.ModerateMembers)]
    public async Task DesaislarAsync(
        InteractionContext ctx,
        [Option("usuario", "Usuario a desaislar")] DiscordUser usuario,
        [Option("motivo", "Motivo")] string? motivo = null)
    {
        motivo ??= _msg.Get("Moderacion:MotivoPorDefecto");

        var miembro = await ValidarObjetivoAsync(ctx, usuario);
        if (miembro is null) return;

        await miembro.TimeoutAsync(null, motivo);

        var incidente = await _log.RegistrarAsync(ctx.Guild.Id, usuario, ctx.User, IncidentType.FinAislamiento, motivo);
        await _log.AnunciarAsync(ctx.Guild, incidente);
        await ResponderExitoAsync(ctx,
            _msg.Get("Moderacion:Exito:FinAislamiento", ("usuario", usuario.Username)), incidente);
    }

    [SlashCommand("advertir", "Registra una advertencia a un usuario")]
    [SlashRequirePermissions(Permissions.ModerateMembers)]
    public async Task AdvertirAsync(
        InteractionContext ctx,
        [Option("usuario", "Usuario a advertir")] DiscordUser usuario,
        [Option("motivo", "Motivo de la advertencia")] string motivo)
    {
        var miembro = await ValidarObjetivoAsync(ctx, usuario);
        if (miembro is null) return;

        await IntentarAvisoPrivadoAsync(miembro, ctx.Guild.Name,
            _msg.Get("Moderacion:Dm:Acciones:Advertencia"), motivo);

        var incidente = await _log.RegistrarAsync(ctx.Guild.Id, usuario, ctx.User, IncidentType.Advertencia, motivo);
        await _log.AnunciarAsync(ctx.Guild, incidente);
        await ResponderExitoAsync(ctx,
            _msg.Get("Moderacion:Exito:Advertencia", ("usuario", usuario.Username)), incidente);
    }

    [SlashCommand("historial", "Muestra los incidentes de un usuario (o los últimos del servidor)")]
    [SlashRequirePermissions(Permissions.ModerateMembers)]
    public async Task HistorialAsync(
        InteractionContext ctx,
        [Option("usuario", "Usuario a consultar (vacío = últimos del servidor)")] DiscordUser? usuario = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var consulta = db.Incidents.Where(i => i.GuildId == ctx.Guild.Id);
        if (usuario is not null)
            consulta = consulta.Where(i => i.TargetUserId == usuario.Id);

        var ultimos = await consulta.OrderByDescending(i => i.Id).Take(10).ToListAsync();

        var embed = new DiscordEmbedBuilder()
            .WithTitle(usuario is null
                ? _msg.Get("Moderacion:Historial:TituloServidor")
                : _msg.Get("Moderacion:Historial:TituloUsuario", ("usuario", usuario.Username)))
            .WithColor(DiscordColor.Blurple);

        if (ultimos.Count == 0)
        {
            embed.WithDescription(_msg.Get("Moderacion:Historial:Vacio"));
        }
        else
        {
            foreach (var i in ultimos)
            {
                var duracion = i.Duration is { } d ? $" · {DurationParser.Format(d)}" : "";
                var cabecera = _msg.Get("Moderacion:Historial:CabeceraCaso",
                    ("caso", i.Id),
                    ("tipo", _msg.Get($"Moderacion:Tipos:{i.Type}")),
                    ("duracion", duracion),
                    ("fecha", $"<t:{i.CreatedAt.ToUnixTimeSeconds()}:d>"));
                var linea = _msg.Get("Moderacion:Historial:Linea",
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
            await ResponderErrorAsync(ctx, _msg.Get("Moderacion:Errores:EsOwner"));
            return false;
        }
        if (miembro.Hierarchy >= ctx.Guild.CurrentMember.Hierarchy)
        {
            await ResponderErrorAsync(ctx,
                _msg.Get("Moderacion:Errores:Jerarquia", ("usuario", miembro.Username)));
            return false;
        }
        return true;
    }

    /// <summary>Valida que el objetivo sea un miembro del servidor sobre el que se pueda actuar.</summary>
    private async Task<DiscordMember?> ValidarObjetivoAsync(InteractionContext ctx, DiscordUser usuario)
    {
        if (usuario.Id == ctx.User.Id)
        {
            await ResponderErrorAsync(ctx, _msg.Get("Moderacion:Errores:MismoUsuario"));
            return null;
        }
        if (usuario.Id == ctx.Client.CurrentUser.Id)
        {
            await ResponderErrorAsync(ctx, _msg.Get("Moderacion:Errores:AlBot"));
            return null;
        }

        var miembro = await BuscarMiembroAsync(ctx, usuario.Id);
        if (miembro is null)
        {
            await ResponderErrorAsync(ctx,
                _msg.Get("Moderacion:Errores:NoEnServidor", ("usuario", usuario.Username)));
            return null;
        }

        return await ValidarJerarquiaAsync(ctx, miembro) ? miembro : null;
    }

    /// <summary>Intenta avisar al usuario por MD antes de la acción (si tiene los MD abiertos).</summary>
    private async Task IntentarAvisoPrivadoAsync(
        DiscordMember miembro, string servidor, string accion, string motivo)
    {
        try
        {
            var dm = await miembro.CreateDmChannelAsync();
            var embed = new DiscordEmbedBuilder()
                .WithTitle(_msg.Get("Moderacion:Dm:Titulo",
                    ("accion", accion), ("servidor", servidor)))
                .WithColor(DiscordColor.Red)
                .AddField(_msg.Get("Moderacion:Dm:CampoMotivo"), motivo);
            await dm.SendMessageAsync(embed);
        }
        catch
        {
            // Tiene los mensajes directos cerrados: se continúa sin avisar.
        }
    }

    private async Task ResponderExitoAsync(InteractionContext ctx, string texto, Incident incidente)
    {
        var embed = new DiscordEmbedBuilder()
            .WithDescription(_msg.Get("Moderacion:Exito:Formato",
                ("texto", texto), ("motivo", incidente.Reason)))
            .WithFooter(_msg.Get("Moderacion:Caso", ("caso", incidente.Id)))
            .WithColor(DiscordColor.Green);

        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(embed));
    }

    private async Task ResponderErrorAsync(InteractionContext ctx, string mensaje)
    {
        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent($"❌ {mensaje}").AsEphemeral());
    }
}
