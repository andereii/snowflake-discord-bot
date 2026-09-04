using System.Collections.Concurrent;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Snowflake.Bot.Data;
using Snowflake.Bot.Data.Entities;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Services;

/// <summary>
/// Servicio de cumpleaños: registra la fecha de cada usuario, expone
/// helpers de consulta y publica la felicitación diaria del servidor.
/// </summary>
public sealed class BirthdayService(
    IDbContextFactory<BotDbContext> dbFactory,
    MessagesService msg,
    ILogger<BirthdayService> logger,
    DiscordClient client)
{
    /// <summary>
    /// Conjunto de cumpleaños del día, indexado por (GuildId, Month, Day).
    /// Se rellena bajo demanda al consultar.
    /// </summary>
    private readonly ConcurrentDictionary<(ulong GuildId, int Month, int Day), List<Birthday>> _byDate = new();

    /// <summary>Config por servidor (cargada bajo demanda).</summary>
    private readonly ConcurrentDictionary<ulong, BirthdayConfig> _configs = new();

    /// <summary>Última vez que se publicó la felicitación de cada servidor (evita duplicados).</summary>
    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _lastPosted = new();

    // ----------------------------- Registro -----------------------------

    /// <summary>
    /// Registra el cumpleaños de un usuario en el servidor.
    /// Devuelve (ok, mensajeError). Si ok=false, el mensaje se muestra al usuario.
    /// </summary>
    public async Task<(bool ok, string? errorKey, object? errorArgs)> RegistrarAsync(
        ulong guildId, ulong userId, int day, int month, int? year)
    {
        if (month < 1 || month > 12)
            return (false, "Cumple:ErrorMes", null);
        if (day < 1 || day > 31)
            return (false, "Cumple:ErrorDia", null);
        var diasPorMes = new[] { 31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
        if (day > diasPorMes[month - 1])
            return (false, "Cumple:ErrorFechaInvalida", null);
        if (year is { } y && (y < 1900 || y > DateTime.UtcNow.Year))
            return (false, "Cumple:ErrorAnio", null);

        await using var db = await dbFactory.CreateDbContextAsync();
        var existente = await db.Birthdays.FindAsync(guildId, userId);
        if (existente is null)
        {
            db.Birthdays.Add(new Birthday
            {
                GuildId = guildId,
                UserId = userId,
                Day = day,
                Month = month,
                Year = year
            });
        }
        else
        {
            existente.Day = day;
            existente.Month = month;
            existente.Year = year;
        }
        await db.SaveChangesAsync();

        // Invalidar caché.
        _byDate.TryRemove((guildId, existente?.Month ?? month, existente?.Day ?? day), out _);
        _byDate.TryRemove((guildId, month, day), out _);

        return (true, null, null);
    }

    /// <summary>Quita el cumpleaños de un usuario en el servidor.</summary>
    public async Task<bool> QuitarAsync(ulong guildId, ulong userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existente = await db.Birthdays.FindAsync(guildId, userId);
        if (existente is null) return false;
        db.Birthdays.Remove(existente);
        await db.SaveChangesAsync();
        _byDate.TryRemove((guildId, existente.Month, existente.Day), out _);
        return true;
    }

    /// <summary>Devuelve el cumpleaños de un usuario (null si no tiene).</summary>
    public async Task<Birthday?> ObtenerAsync(ulong guildId, ulong userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Birthdays.AsNoTracking().FirstOrDefaultAsync(b => b.GuildId == guildId && b.UserId == userId);
    }

    // ----------------------------- Config -----------------------------

    public async Task<BirthdayConfig> ObtenerConfigAsync(ulong guildId)
    {
        if (_configs.TryGetValue(guildId, out var cached)) return cached;

        await using var db = await dbFactory.CreateDbContextAsync();
        var cfg = await db.BirthdayConfigs.FindAsync(guildId)
            ?? new BirthdayConfig { GuildId = guildId };
        _configs[guildId] = cfg;
        return cfg;
    }

    public async Task ActualizarConfigAsync(ulong guildId, Action<BirthdayConfig> mutar)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var cfg = await db.BirthdayConfigs.FindAsync(guildId);
        if (cfg is null)
        {
            cfg = new BirthdayConfig { GuildId = guildId };
            db.BirthdayConfigs.Add(cfg);
        }
        mutar(cfg);
        await db.SaveChangesAsync();
        _configs[guildId] = cfg;
    }

    // ----------------------------- Felicitación diaria -----------------------------

    /// <summary>
    /// Publica los mensajes de felicitación del día en los servidores donde
    /// esté habilitado. Llamar cada hora (o más seguido) desde un BackgroundService.
    /// </summary>
    public async Task PublicarCumplesDelDiaAsync(CancellationToken ct = default)
    {
        var ahora = DateTimeOffset.UtcNow;
        var mes = ahora.Month;
        var dia = ahora.Day;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cumpleaneros = await db.Birthdays
            .Where(b => b.Month == mes && b.Day == dia)
            .ToListAsync(ct);

        if (cumpleaneros.Count == 0) return;

        // Agrupar por servidor.
        foreach (var grupo in cumpleaneros.GroupBy(b => b.GuildId))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await PublicarEnServidorAsync(grupo.Key, grupo.ToList(), ahora, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error publicando cumple en {Guild}", grupo.Key);
            }
        }
    }

    private async Task PublicarEnServidorAsync(ulong guildId, List<Birthday> cumpleaneros, DateTimeOffset ahora, CancellationToken ct)
    {
        var cfg = await ObtenerConfigAsync(guildId);
        if (!cfg.Enabled || cfg.ChannelId is null) return;

        // Evitar publicar dos veces el mismo día.
        if (_lastPosted.TryGetValue(guildId, out var last) && last.UtcDateTime.Date == ahora.UtcDateTime.Date)
            return;
        if (ahora.Hour < cfg.HourUtc) return;

        if (!client.Guilds.TryGetValue(guildId, out var guild)) return;
        DiscordChannel? canal;
        try { canal = await client.GetChannelAsync(cfg.ChannelId.Value); }
        catch { return; }
        if (canal is null) return;

        foreach (var b in cumpleaneros)
        {
            DiscordMember? miembro;
            try { miembro = await guild.GetMemberAsync(b.UserId); }
            catch { continue; }
            if (miembro is null) continue;

            var edad = b.Year is { } y ? (DateTime.UtcNow.Year - y).ToString() : null;
            var texto = cfg.Message
                .Replace("{usuario}", miembro.Mention)
                .Replace("{servidor}", guild.Name)
                .Replace("{edad}", edad ?? "")
                .Trim();

            await canal.SendMessageAsync(texto);
        }

        _lastPosted[guildId] = ahora;
    }
}
