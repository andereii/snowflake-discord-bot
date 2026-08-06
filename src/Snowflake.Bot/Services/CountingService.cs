using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Snowflake.Bot.Data;
using Snowflake.Bot.Data.Entities;

namespace Snowflake.Bot.Services;

/// <summary>
/// Juego de conteo: el siguiente usuario debe escribir el número que sigue,
/// en la base configurada. Detecta errores, récords, oportunidades extra
/// diarias y lleva estadísticas por usuario.
/// </summary>
public sealed partial class CountingService(
    DiscordClient client,
    IDbContextFactory<BotDbContext> dbFactory,
    MessagesService msg,
    ILogger<CountingService> logger)
{
    // Un candado por servidor para serializar el conteo y evitar carreras.
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _locks = new();

    // ---------------------- Conversión de bases ----------------------

    public static int BaseRadix(CountingBase b) => b switch
    {
        CountingBase.Binario => 2,
        CountingBase.Octal => 8,
        CountingBase.Hexadecimal => 16,
        _ => 10
    };

    /// <summary>Formatea un valor en la base indicada (hex en mayúsculas).</summary>
    public static string Formatear(long valor, CountingBase b) => b switch
    {
        CountingBase.Binario => Convert.ToString(valor, 2),
        CountingBase.Octal => Convert.ToString(valor, 8),
        CountingBase.Hexadecimal => valor.ToString("X", CultureInfo.InvariantCulture),
        _ => valor.ToString("0", CultureInfo.InvariantCulture)
    };

    /// <summary>Intenta interpretar un texto como número en la base dada.</summary>
    public static bool IntentarParsear(string texto, CountingBase b, out long valor)
    {
        valor = 0;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        try
        {
            valor = Convert.ToInt64(texto.Trim(), BaseRadix(b));
            return valor > 0; // la cuenta empieza en 1; no aceptamos 0 ni negativos
        }
        catch { return false; }
    }

    // ---------------------- Emojis ----------------------

    [GeneratedRegex(@"^<a?:(\w+):(\d+)>$", RegexOptions.IgnoreCase)]
    private static partial Regex EmojiRegex();

    /// <summary>Comprueba si un texto es un emoji válido (unicode o personalizado).</summary>
    public static bool EmojiValido(DiscordClient c, string s)
    {
        s = s.Trim();
        try { DiscordEmoji.FromUnicode(c, s); return true; } catch { }
        try { DiscordEmoji.FromName(c, s); return true; } catch { }
        var m = EmojiRegex().Match(s);
        return m.Success;
    }

    /// <summary>Construye un DiscordEmoji desde la config (con fallback al unicode por defecto).</summary>
    private DiscordEmoji ParseEmoji(string? s, string fallback)
    {
        var str = string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();
        try { return DiscordEmoji.FromUnicode(client, str); } catch { }
        try { return DiscordEmoji.FromName(client, str); } catch { }
        var m = EmojiRegex().Match(str);
        if (m.Success && ulong.TryParse(m.Groups[2].Value, out var id))
        {
            try { return DiscordEmoji.FromGuildEmote(client, id); } catch { }
        }
        try { return DiscordEmoji.FromUnicode(client, fallback); } catch { }
        return DiscordEmoji.FromUnicode(client, "✅");
    }

    // ---------------------- Procesamiento de mensajes ----------------------

    /// <summary>Punto de entrada: se llama desde MessageCreated.</summary>
    public async Task ProcesarMensajeAsync(DiscordMessage message)
    {
        try
        {
            if (message.Author?.IsBot == true) return;
            var channel = message.Channel;
            if (channel is null) return;
            var guild = channel.Guild;
            if (guild is null) return; // mensaje privado

            var sem = _locks.GetOrAdd(guild.Id, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync();
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var cfg = await db.CountingConfigs.FindAsync(guild.Id);
                if (cfg is null || cfg.ChannelId is null || message.ChannelId != cfg.ChannelId.Value) return;

                await ProcesarAsync(message, cfg, db);
                await db.SaveChangesAsync();
            }
            finally { sem.Release(); }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error procesando conteo");
        }
    }

    private async Task ProcesarAsync(DiscordMessage message, CountingConfig cfg, BotDbContext db)
    {
        // Reseteo diario de oportunidades extra.
        var hoy = DateTimeOffset.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (cfg.LastExtraChanceResetDate != hoy)
        {
            cfg.ExtraChancesUsedToday = 0;
            cfg.LastExtraChanceResetDate = hoy;
        }

        // Estadísticas del usuario (se crean si no existían).
        var stat = await db.CountingStats.FirstOrDefaultAsync(
            s => s.GuildId == cfg.GuildId && s.UserId == message.Author.Id);
        if (stat is null)
        {
            stat = new CountingStat { GuildId = cfg.GuildId, UserId = message.Author.Id };
            db.CountingStats.Add(stat);
        }

        // Solo se procesa si el mensaje es un número válido en la base actual.
        if (!IntentarParsear(message.Content, cfg.Base, out var valor)) return;

        var esperado = cfg.CurrentValue + 1;
        var mismoUsuario = cfg.LastUserId == message.Author.Id;

        if (valor == esperado && !mismoUsuario)
        {
            await CorrectoAsync(message, cfg, stat, valor);
        }
        else
        {
            await IncorrectoAsync(message, cfg, stat, valor, mismoUsuario);
        }
    }

    private async Task CorrectoAsync(DiscordMessage message, CountingConfig cfg, CountingStat stat, long valor)
    {
        // ¿Es un número que supera el récord que había antes de empezar esta cadena?
        var esNuevoRecordHistorico = valor > cfg.RecordAtChainStart && cfg.RecordAtChainStart > 0;
        
        var debeAnunciarRecord = esNuevoRecordHistorico && !cfg.RecordCelebratedThisChain;

        // El récord histórico crece si supera al anterior.
        if (valor > cfg.CurrentRecord) cfg.CurrentRecord = valor;
        if (debeAnunciarRecord) cfg.RecordCelebratedThisChain = true;

        cfg.CurrentValue = valor;
        cfg.LastUserId = message.Author.Id;

        stat.TotalCounts++;
        stat.BestContribution = Math.Max(stat.BestContribution, valor);

        // Reacción: si supera el récord histórico base, usa el emoji de récord.
        var emoji = esNuevoRecordHistorico
            ? ParseEmoji(cfg.EmojiRecord, "🎉")
            : ParseEmoji(cfg.EmojiCorrect, "✅");
        await ReaccionarAsync(message, emoji);

        if (debeAnunciarRecord)
            await EnviarAsync(message.Channel, msg.Get("Conteo:RecordAlcanzado", ("cuenta", Formatear(valor, cfg.Base))));

        if (cfg.Goal is { } meta && valor == meta)
            await EnviarAsync(message.Channel, msg.Get("Conteo:ObjetivoAlcanzado", ("objetivo", Formatear(meta, cfg.Base))));
    }

    private async Task IncorrectoAsync(
        DiscordMessage message, CountingConfig cfg, CountingStat stat, long valor, bool mismoUsuario)
    {
        // ¿Es la primera vez que este usuario interactúa con la cuenta?
        // (Sin aciertos previos y sin errores previos.) Se le perdona y se le
        // envía un aviso privado con el número correcto. Solo ocurre una vez.
        var esPrimeraVez = stat.TotalCounts == 0 && stat.IncorrectCounts == 0;
        stat.IncorrectCounts++;

        if (esPrimeraVez)
        {
            await ReaccionarAsync(message, DiscordEmoji.FromUnicode(client, "🛡️"));
            var siguienteHint = Formatear(cfg.CurrentValue + 1, cfg.Base);
            await EnviarHintPrivadoAsync(message, siguienteHint);
            return; // se perdona: la cadena sigue, el siguiente esperado no cambia
        }

        // ¿Hay oportunidad extra disponible hoy?
        bool perdonado = cfg.ExtraChancesPerDay > 0
            && cfg.ExtraChancesUsedToday < cfg.ExtraChancesPerDay;

        if (perdonado)
        {
            cfg.ExtraChancesUsedToday++;
            await ReaccionarAsync(message, DiscordEmoji.FromUnicode(client, "🛡️"));
            return; // se perdona: la cadena sigue, el siguiente esperado no cambia
        }

        // No perdonado: se pierde la cuenta.
        await ReaccionarAsync(message, ParseEmoji(cfg.EmojiIncorrect, "❌"));

        var cuentaFormateada = Formatear(cfg.CurrentValue, cfg.Base);
        var siguiente = Formatear(1, cfg.Base);

        var texto = mismoUsuario
            ? msg.Get("Conteo:MismoUsuario",
                ("usuario", message.Author.Mention), ("siguiente", siguiente))
            : (string.IsNullOrWhiteSpace(cfg.LoseMessage)
                ? msg.Get("Conteo:Perdiste",
                    ("cuenta", cuentaFormateada),
                    ("usuario", message.Author.Mention),
                    ("siguiente", siguiente))
                : cfg.LoseMessage!
                    .Replace("{cuenta}", cuentaFormateada)
                    .Replace("{usuario}", message.Author.Mention)
                    .Replace("{siguiente}", siguiente));

        await EnviarAsync(message.Channel, texto);

        // Reset de la cadena. El récord histórico se conserva.
        cfg.CurrentValue = 0;
        cfg.LastUserId = null;
        cfg.RecordAtChainStart = cfg.CurrentRecord;
        cfg.RecordCelebratedThisChain = false;
    }

    private async Task ReaccionarAsync(DiscordMessage msg, DiscordEmoji emoji)
    {
        try { await msg.CreateReactionAsync(emoji); } catch { /* rate-limit o emoji inválido */ }
    }

    private static async Task EnviarAsync(DiscordChannel canal, string contenido)
    {
        try { await canal.SendMessageAsync(contenido); } catch { }
    }

    /// <summary>
    /// Envía la pista del "perdón de primera vez" al usuario por mensaje
    /// directo (solo lo ve él). Si no tiene los MD abiertos, hace fallback a
    /// una respuesta en el canal mencionándolo.
    /// </summary>
    private async Task EnviarHintPrivadoAsync(DiscordMessage message, string siguiente)
    {
        var texto = msg.Get("Conteo:PrimeraVezHint", ("siguiente", siguiente));
        try
        {
            DiscordMember miembro;
            if (message.Author is DiscordMember m)
                miembro = m;
            else
            {
                var guild = message.Channel.Guild
                    ?? throw new InvalidOperationException("El conteo solo ocurre en servidores.");
                miembro = await guild.GetMemberAsync(message.Author.Id);
            }

            var dm = await miembro.CreateDmChannelAsync();
            await dm.SendMessageAsync(texto);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "No se pudo enviar hint por DM; fallback a canal");
            try { await message.RespondAsync(texto); } catch { }
        }
    }

    // ---------------------- Consultas para los comandos ----------------------

    /// <summary>Construye el embed del leaderboard (top 10 por aportes correctos).</summary>
    public async Task<DiscordEmbedBuilder?> ConstruirLeaderboardAsync(DiscordGuild guild)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var top = await db.CountingStats
            .Where(s => s.GuildId == guild.Id && s.TotalCounts > 0)
            .OrderByDescending(s => s.TotalCounts)
            .Take(10)
            .ToListAsync();

        if (top.Count == 0) return null;

        var embed = new DiscordEmbedBuilder()
            .WithTitle(msg.Get("Conteo:LeaderboardTitulo"))
            .WithColor(DiscordColor.Blurple);

        var sb = new StringBuilder();
        for (var i = 0; i < top.Count; i++)
        {
            var nombre = await NombreAsync(guild, top[i].UserId);
            sb.AppendLine($"{Medalla(i + 1)} {nombre} — **{top[i].TotalCounts:N0}**");
        }
        embed.WithDescription(sb.ToString());
        return embed;
    }

    /// <summary>Construye el embed de estadísticas de un usuario.</summary>
    public async Task<DiscordEmbedBuilder?> ConstruirStatsAsync(DiscordGuild guild, ulong userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var cfg = await db.CountingConfigs.FindAsync(guild.Id);
        var base_ = cfg?.Base ?? CountingBase.Decimal;

        var s = await db.CountingStats.FirstOrDefaultAsync(x => x.GuildId == guild.Id && x.UserId == userId);
        var nombre = await NombreAsync(guild, userId);

        if (s is null || (s.TotalCounts == 0 && s.IncorrectCounts == 0))
        {
            // Embed vacío con aviso.
            return new DiscordEmbedBuilder()
                .WithTitle(msg.Get("Conteo:StatsTitulo", ("usuario", nombre)))
                .WithDescription(msg.Get("Conteo:StatsSinDatos"))
                .WithColor(DiscordColor.Blurple);
        }

        var total = s.TotalCounts + s.IncorrectCounts;
        var precision = total == 0 ? 100.0 : s.TotalCounts * 100.0 / total;

        return new DiscordEmbedBuilder()
            .WithTitle(msg.Get("Conteo:StatsTitulo", ("usuario", nombre)))
            .WithColor(DiscordColor.Blurple)
            .AddField(msg.Get("Conteo:StatsTotal"), s.TotalCounts.ToString("N0", CultureInfo.InvariantCulture), true)
            .AddField(msg.Get("Conteo:StatsIncorrectos"), s.IncorrectCounts.ToString("N0", CultureInfo.InvariantCulture), true)
            .AddField(msg.Get("Conteo:StatsPrecision"), $"{precision:0.#}%", true)
            .AddField(msg.Get("Conteo:StatsMejor"), Formatear(s.BestContribution, base_), true);
    }

    private async Task<string> NombreAsync(DiscordGuild g, ulong uid)
    {
        try { var m = await g.GetMemberAsync(uid); return m.DisplayName ?? m.Username; }
        catch { return $"<@{uid}>"; }
    }

    private static string Medalla(int puesto) => puesto switch
    {
        1 => "🥇",
        2 => "🥈",
        3 => "🥉",
        _ => $"`#{puesto}`"
    };
}