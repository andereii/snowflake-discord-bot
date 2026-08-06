using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Microsoft.EntityFrameworkCore;
using Snowflake.Bot.Data;
using Snowflake.Bot.Data.Entities;
using Snowflake.Bot.Services;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Juego de conteo con récords, estadísticas, bases alternativas y oportunidades extra.
/// Comandos: /counting canal · desactivar · base · oportunidades · objetivo · objetivo-quitar
///           · iconos · mensaje-perdida · leaderboard · estadisticas
/// </summary>
[SlashCommandGroup("counting", "Configura y juega al conteo en el servidor")]
public sealed class CountingModule : ApplicationCommandModule
{
    private readonly IDbContextFactory<BotDbContext> _dbFactory;
    private readonly CountingService _counting;
    private readonly MessagesService _msg;

    public CountingModule(IDbContextFactory<BotDbContext> dbFactory, CountingService counting, MessagesService msg)
    {
        _dbFactory = dbFactory;
        _counting = counting;
        _msg = msg;
    }

    // ------------------------- Configuración (admins) -------------------------

    [SlashCommand("canal", "Establece el canal donde se contará")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task CanalAsync(
        InteractionContext ctx,
        [Option("canal", "Canal de texto para el conteo")] DiscordChannel canal)
    {
        if (canal.Type != ChannelType.Text)
        {
            await ResponderAsync(ctx, _msg.Get("Conteo:CanalDebeSerTexto"), ephemeral: true);
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var cfg = await GetOrCreateAsync(db, ctx.Guild.Id);
        cfg.ChannelId = canal.Id;
        await db.SaveChangesAsync();

        await ResponderAsync(ctx, _msg.Get("Conteo:CanalEstablecido", ("canal", canal.Mention)));
    }

    [SlashCommand("desactivar", "Desenlaza el canal y deja de leer el conteo")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task DesactivarAsync(InteractionContext ctx)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var cfg = await db.CountingConfigs.FindAsync(ctx.Guild.Id);

        if (cfg is null || cfg.ChannelId is null)
        {
            await ResponderAsync(ctx, _msg.Get("Conteo:YaDesactivado"), ephemeral: true);
            return;
        }

        cfg.ChannelId = null;
        await db.SaveChangesAsync();

        await ResponderAsync(ctx, _msg.Get("Conteo:Desactivado"));
    }

    [SlashCommand("base", "Cambia el modo de juego (decimal, binario, octal, hexadecimal)")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task BaseAsync(
        InteractionContext ctx,
        [Option("base", "Base en la que contar")]
        [Choice("Decimal", "decimal"), Choice("Binario", "binario"), Choice("Octal", "octal"), Choice("Hexadecimal", "hexadecimal")]
        string base_)
    {
        var tipo = base_ switch
        {
            "binario" => CountingBase.Binario,
            "octal" => CountingBase.Octal,
            "hexadecimal" => CountingBase.Hexadecimal,
            _ => CountingBase.Decimal
        };

        await using var db = await _dbFactory.CreateDbContextAsync();
        var cfg = await GetOrCreateAsync(db, ctx.Guild.Id);
        cfg.Base = tipo;
        await db.SaveChangesAsync();

        await ResponderAsync(ctx, _msg.Get("Conteo:BaseEstablecida", ("base", Capitalizar(base_))));
    }

    [SlashCommand("oportunidades", "Oportunidades extra diarias que perdonan un error (0-10, 0 = desactivado)")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task OportunidadesAsync(
        InteractionContext ctx,
        [Option("cantidad", "Número de perdones al día (0 a 10)")] long cantidad)
    {
        if (cantidad < 0 || cantidad > 10)
        {
            await ResponderAsync(ctx, _msg.Get("Conteo:OportunidadesRango"), ephemeral: true);
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var cfg = await GetOrCreateAsync(db, ctx.Guild.Id);
        cfg.ExtraChancesPerDay = (int)cantidad;
        await db.SaveChangesAsync();

        await ResponderAsync(ctx, _msg.Get("Conteo:OportunidadesActualizadas", ("n", cantidad)));
    }

    [SlashCommand("objetivo", "Establece un objetivo numérico para el servidor")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task ObjetivoAsync(
        InteractionContext ctx,
        [Option("numero", "Número objetivo que alcanzar")] long numero)
    {
        if (numero <= 0)
        {
            await ResponderAsync(ctx, _msg.Get("Conteo:ObjetivoInvalido"), ephemeral: true);
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var cfg = await GetOrCreateAsync(db, ctx.Guild.Id);
        cfg.Goal = numero;
        await db.SaveChangesAsync();

        await using var db2 = await _dbFactory.CreateDbContextAsync();
        var cfg2 = await db2.CountingConfigs.FindAsync(ctx.Guild.Id);
        await ResponderAsync(ctx, _msg.Get("Conteo:ObjetivoEstablecido",
            ("objetivo", CountingService.Formatear(numero, cfg2?.Base ?? CountingBase.Decimal))));
    }

    [SlashCommand("objetivo-quitar", "Elimina el objetivo del servidor")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task ObjetivoQuitarAsync(InteractionContext ctx)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var cfg = await GetOrCreateAsync(db, ctx.Guild.Id);
        cfg.Goal = null;
        await db.SaveChangesAsync();

        await ResponderAsync(ctx, _msg.Get("Conteo:ObjetivoQuitado"));
    }

    [SlashCommand("iconos", "Elige los emojis con los que el bot reacciona (correcto, incorrecto, récord)")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task IconosAsync(
        InteractionContext ctx,
        [Option("correcto", "Emoji de respuesta correcta (por defecto ✅)")] string? correcto = null,
        [Option("incorrecto", "Emoji de respuesta incorrecta (por defecto ❌)")] string? incorrecto = null,
        [Option("record", "Emoji de nuevo récord (por defecto 🎉)")] string? record = null)
    {
        // Validar los que se hayan proporcionado.
        if (correcto is not null && !CountingService.EmojiValido(ctx.Client, correcto)
            || incorrecto is not null && !CountingService.EmojiValido(ctx.Client, incorrecto)
            || record is not null && !CountingService.EmojiValido(ctx.Client, record))
        {
            await ResponderAsync(ctx, _msg.Get("Conteo:IconoInvalido"), ephemeral: true);
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var cfg = await GetOrCreateAsync(db, ctx.Guild.Id);
        if (correcto is not null) cfg.EmojiCorrect = correcto.Trim();
        if (incorrecto is not null) cfg.EmojiIncorrect = incorrecto.Trim();
        if (record is not null) cfg.EmojiRecord = record.Trim();
        await db.SaveChangesAsync();

        await ResponderAsync(ctx, _msg.Get("Conteo:IconosActualizados",
            ("correcto", correcto ?? "✅"),
            ("incorrecto", incorrecto ?? "❌"),
            ("record", record ?? "🎉")));
    }

    [SlashCommand("mensaje-perdida", "Personaliza el mensaje al perder la cuenta (placeholders: {cuenta} {usuario} {siguiente})")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task MensajePerdidaAsync(
        InteractionContext ctx,
        [Option("mensaje", "Nuevo mensaje. Vacío = restablecer al por defecto.")]
        string? mensaje = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var cfg = await GetOrCreateAsync(db, ctx.Guild.Id);

        if (string.IsNullOrWhiteSpace(mensaje))
        {
            cfg.LoseMessage = null;
            await db.SaveChangesAsync();
            await ResponderAsync(ctx, _msg.Get("Conteo:MensajePerdidaBorrado"));
            return;
        }

        cfg.LoseMessage = mensaje;
        await db.SaveChangesAsync();

        // Vista previa sustituyendo con quien ejecuta el comando.
        var cfg2 = cfg;
        var vista = mensaje
            .Replace("{cuenta}", CountingService.Formatear(42, cfg2.Base))
            .Replace("{usuario}", ctx.User.Mention)
            .Replace("{siguiente}", CountingService.Formatear(1, cfg2.Base));

        await ResponderAsync(ctx, _msg.Get("Conteo:MensajePerdidaGuardado", ("vista", vista)));
    }

    // ------------------------- Consulta (todos) -------------------------

    [SlashCommand("leaderboard", "Muestra quién más ha aportado a la cuenta")]
    public async Task LeaderboardAsync(InteractionContext ctx)
    {
        var embed = await _counting.ConstruirLeaderboardAsync(ctx.Guild);
        if (embed is null)
        {
            await ResponderAsync(ctx, _msg.Get("Conteo:LeaderboardVacio"), ephemeral: true);
            return;
        }
        await ResponderAsync(ctx, embed);
    }

    [SlashCommand("estadisticas", "Muestra las estadísticas de un usuario (o de ti mismo)")]
    public async Task EstadisticasAsync(
        InteractionContext ctx,
        [Option("usuario", "Usuario a consultar (vacío = tú mismo)")] DiscordUser? usuario = null)
    {
        var uid = usuario?.Id ?? ctx.User.Id;
        var embed = await _counting.ConstruirStatsAsync(ctx.Guild, uid);
        if (embed is null)
        {
            await ResponderAsync(ctx, _msg.Get("Conteo:StatsSinDatos"), ephemeral: true);
            return;
        }
        await ResponderAsync(ctx, embed);
    }

    // ------------------------- Ayudantes -------------------------

    private static async Task<CountingConfig> GetOrCreateAsync(BotDbContext db, ulong guildId)
    {
        var cfg = await db.CountingConfigs.FindAsync(guildId);
        if (cfg is null)
        {
            cfg = new CountingConfig { GuildId = guildId };
            db.CountingConfigs.Add(cfg);
        }
        return cfg;
    }

    private static string Capitalizar(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static async Task ResponderAsync(InteractionContext ctx, string contenido, bool ephemeral = false)
    {
        var b = new DiscordInteractionResponseBuilder().WithContent(contenido);
        if (ephemeral) b.AsEphemeral();
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, b);
    }

    private static async Task ResponderAsync(InteractionContext ctx, DiscordEmbedBuilder embed)
    {
        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(embed));
    }
}