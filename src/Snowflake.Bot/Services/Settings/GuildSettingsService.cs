using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Snowflake.Bot.Configuration;
using Snowflake.Bot.Data;
using Snowflake.Bot.Data.Entities;

namespace Snowflake.Bot.Services.Settings;

/// <summary>
/// Punto único de acceso a los ajustes por servidor. Toda lectura/escritura de
/// configuración (desde el bot HOY, desde un panel web MAÑANA) pasa por aquí.
///
/// Diseño:
/// - GuildConfig se cachea en memoria con TTL (SettingsCacheSeconds): se lee en
///   la ruta caliente de cada mensaje de chat y cambia muy raramente.
/// - CountingConfig y YouTubeSubscription NO se cachean: la primera cambia con
///   cada mensaje del juego y la segunda se actualiza sola en background.
/// - Las mutaciones se hacen siempre mediante Update*Async, que guardan en BD y
///   refrescan/invalidan la caché de forma atómica respecto al resto del bot.
///
/// El snapshot (GetSnapshotAsync) es el contrato JSON que consumirá el panel web.
/// </summary>
public sealed class GuildSettingsService(
    IDbContextFactory<BotDbContext> dbFactory,
    IOptionsMonitor<BotConfiguration> botConfig)
{
    private sealed record CacheEntry(GuildConfig Config, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<ulong, CacheEntry> _cache = new();

    // ------------------------- GuildConfig (cacheada) -------------------------

    /// <summary>
    /// Devuelve la configuración general del servidor (copia desconectada:
    /// los cambios que le hagas NO se guardan; usa <see cref="UpdateAsync"/>).
    /// Si nunca se configuró, devuelve una instancia con los valores por defecto.
    /// </summary>
    public async Task<GuildConfig> GetAsync(ulong guildId, CancellationToken ct = default)
    {
        var ttl = TimeSpan.FromSeconds(Math.Max(1, botConfig.CurrentValue.SettingsCacheSeconds));

        if (_cache.TryGetValue(guildId, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
            return Clone(entry.Config);

        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var cfg = await db.GuildConfigs.AsNoTracking().FirstOrDefaultAsync(g => g.GuildId == guildId, ct)
            .ConfigureAwait(false) ?? new GuildConfig { GuildId = guildId };

        _cache[guildId] = new CacheEntry(Clone(cfg), DateTimeOffset.UtcNow + ttl);
        return cfg;
    }

    /// <summary>
    /// Aplica una mutación a la configuración general del servidor, la guarda
    /// en la base de datos y actualiza la caché. Crea la fila si no existía.
    /// </summary>
    public async Task<GuildConfig> UpdateAsync(
        ulong guildId, Action<GuildConfig> mutate, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var cfg = await db.GuildConfigs.FindAsync([guildId], ct).ConfigureAwait(false);
        if (cfg is null)
        {
            cfg = new GuildConfig { GuildId = guildId };
            db.GuildConfigs.Add(cfg);
        }

        mutate(cfg);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var ttl = TimeSpan.FromSeconds(Math.Max(1, botConfig.CurrentValue.SettingsCacheSeconds));
        _cache[guildId] = new CacheEntry(Clone(cfg), DateTimeOffset.UtcNow + ttl);
        return Clone(cfg);
    }

    /// <summary>Descarta la entrada cacheada del servidor (la próxima lectura irá a BD).</summary>
    public void Invalidate(ulong guildId) => _cache.TryRemove(guildId, out _);

    // ------------------- CountingConfig / YouTube (sin caché) -------------------

    /// <summary>
    /// Devuelve la config del juego de conteo (sin cachear: cambia a menudo).
    /// Null si el servidor nunca lo configuró.
    /// </summary>
    public async Task<CountingConfig?> GetCountingAsync(ulong guildId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.CountingConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.GuildId == guildId, ct).ConfigureAwait(false);
    }

    /// <summary>Mutación de la config de conteo (crea la fila si no existía).</summary>
    public async Task<CountingConfig> UpdateCountingAsync(
        ulong guildId, Action<CountingConfig> mutate, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var cfg = await db.CountingConfigs.FindAsync([guildId], ct).ConfigureAwait(false);
        if (cfg is null)
        {
            cfg = new CountingConfig { GuildId = guildId };
            db.CountingConfigs.Add(cfg);
        }
        mutate(cfg);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return cfg;
    }

    /// <summary>Devuelve la suscripción de YouTube del servidor (null si no tiene).</summary>
    public async Task<YouTubeSubscription?> GetYouTubeAsync(ulong guildId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.YouTubeSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(y => y.GuildId == guildId, ct).ConfigureAwait(false);
    }

    /// <summary>Mutación de la suscripción de YouTube (crea la fila si no existía).</summary>
    public async Task<YouTubeSubscription> UpdateYouTubeAsync(
        ulong guildId, Action<YouTubeSubscription> mutate, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var sub = await db.YouTubeSubscriptions.FindAsync([guildId], ct).ConfigureAwait(false);
        if (sub is null)
        {
            sub = new YouTubeSubscription { GuildId = guildId };
            db.YouTubeSubscriptions.Add(sub);
        }
        mutate(sub);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return sub;
    }

    /// <summary>Elimina la suscripción de YouTube del servidor (si existe).</summary>
    public async Task<bool> DeleteYouTubeAsync(ulong guildId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var sub = await db.YouTubeSubscriptions.FindAsync([guildId], ct).ConfigureAwait(false);
        if (sub is null) return false;
        db.YouTubeSubscriptions.Remove(sub);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    // ------------------------- Snapshot para el panel web -------------------------

    /// <summary>
    /// Construye el snapshot serializable con TODOS los ajustes del servidor:
    /// es la respuesta que un panel web mostraría en su pantalla de configuración.
    /// </summary>
    public async Task<GuildSettingsSnapshot> GetSnapshotAsync(ulong guildId, CancellationToken ct = default)
    {
        var cfg = await GetAsync(guildId, ct).ConfigureAwait(false);
        var counting = await GetCountingAsync(guildId, ct).ConfigureAwait(false);
        var youtube = await GetYouTubeAsync(guildId, ct).ConfigureAwait(false);

        List<string> bloqueados;
        await using (var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
        {
            bloqueados = await db.ChannelLocks
                .Where(l => l.GuildId == guildId)
                .Select(l => l.ChannelId.ToString())
                .ToListAsync(ct).ConfigureAwait(false);
        }

        return new GuildSettingsSnapshot
        {
            GuildId = guildId.ToString(),
            Moderation = new GuildSettingsSnapshot.ModerationSection
            {
                LogChannelId = IdToString(cfg.ModLogChannelId)
            },
            Welcome = new GuildSettingsSnapshot.WelcomeSection
            {
                Enabled = cfg.WelcomeChannelId is not null,
                ChannelId = IdToString(cfg.WelcomeChannelId),
                Message = cfg.WelcomeMessage
            },
            Voice = new GuildSettingsSnapshot.VoiceSection
            {
                HubChannelId = IdToString(cfg.HubChannelId),
                TempChannelNameTemplate = cfg.TempChannelNameTemplate
            },
            Music = new GuildSettingsSnapshot.MusicSection
            {
                Volume = cfg.Volume,
                DjRoleId = IdToString(cfg.DjRoleId)
            },
            Ai = new GuildSettingsSnapshot.AiSection
            {
                ChatEnabled = cfg.GeminiChatEnabled,
                MentionsEnabled = cfg.GeminiMentionsEnabled,
                SpontaneousEnabled = cfg.GeminiSpontaneousEnabled
            },
            Downloads = new GuildSettingsSnapshot.DownloadsSection
            {
                Enabled = cfg.DownloadsEnabled
            },
            BlockedChannels = bloqueados,
            Counting = counting is null ? null : new GuildSettingsSnapshot.CountingSection
            {
                Enabled = counting.ChannelId is not null,
                ChannelId = IdToString(counting.ChannelId),
                Base = counting.Base.ToString(),
                Goal = counting.Goal,
                ExtraChancesPerDay = counting.ExtraChancesPerDay,
                EmojiCorrect = counting.EmojiCorrect,
                EmojiIncorrect = counting.EmojiIncorrect,
                EmojiRecord = counting.EmojiRecord,
                LoseMessage = counting.LoseMessage,
                CurrentValue = counting.CurrentValue,
                CurrentRecord = counting.CurrentRecord
            },
            YouTube = youtube is null ? null : new GuildSettingsSnapshot.YouTubeSection
            {
                ChannelId = youtube.YTChannelId,
                ChannelName = youtube.YTChannelName,
                NotifyChannelId = youtube.NotifyChannelId.ToString(),
                NotifyRoleId = IdToString(youtube.NotifyRoleId),
                CustomMessage = youtube.CustomMessage
            }
        };
    }

    // ------------------------- internos -------------------------

    private static string? IdToString(ulong? id) => id?.ToString();

    private static GuildConfig Clone(GuildConfig c) => new()
    {
        GuildId = c.GuildId,
        ModLogChannelId = c.ModLogChannelId,
        WelcomeChannelId = c.WelcomeChannelId,
        WelcomeMessage = c.WelcomeMessage,
        HubChannelId = c.HubChannelId,
        TempChannelNameTemplate = c.TempChannelNameTemplate,
        Volume = c.Volume,
        DjRoleId = c.DjRoleId,
        GeminiChatEnabled = c.GeminiChatEnabled,
        GeminiMentionsEnabled = c.GeminiMentionsEnabled,
        GeminiSpontaneousEnabled = c.GeminiSpontaneousEnabled,
        DownloadsEnabled = c.DownloadsEnabled
    };
}
