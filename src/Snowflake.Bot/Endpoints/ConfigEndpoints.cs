using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Snowflake.Bot.Services.Settings;

namespace Snowflake.Bot.Endpoints;

/// <summary>
/// API REST del panel web de configuración. Todas las lecturas y escrituras
/// pasan por GuildSettingsService, el mismo punto único que usa el bot: así
/// la caché del bot se invalida al guardar desde el panel y nunca hay dos
/// caminos de escritura a la base de datos.
///
/// SEGURIDAD: estos endpoints NO tienen autenticación de usuario (OAuth de
/// Discord) todavía. Si defines la variable de entorno WEB_PANEL_API_KEY, toda
/// mutación exigirá la cabecera "X-Api-Key". Antes de exponer la API a
/// Internet, añade además HTTPS y autenticación por sesión de Discord.
/// </summary>
public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/guilds/{guildId:long}/config");

        // ---------- Vista de panel: TODO el ajuste del servidor en una llamada ----------

        group.MapGet("/", async (ulong guildId, GuildSettingsService settings, CancellationToken ct) =>
        {
            var snapshot = await settings.GetSnapshotAsync(guildId, ct);
            return Results.Ok(snapshot);
        });

        // ---------- Configuración general (patch: solo los campos enviados) ----------

        group.MapPost("/", async (
            ulong guildId,
            GuildConfigPatch patch,
            HttpContext http,
            GuildSettingsService settings,
            CancellationToken ct) =>
        {
            if (!ApiKeyGuard.Autorizado(http)) return Results.Unauthorized();

            var cfg = await settings.UpdateAsync(guildId, c =>
            {
                if (patch.ModLogChannelId is not null) c.ModLogChannelId = ParseId(patch.ModLogChannelId);
                if (patch.WelcomeChannelId is not null) c.WelcomeChannelId = ParseId(patch.WelcomeChannelId);
                if (patch.WelcomeMessage is not null) c.WelcomeMessage = patch.WelcomeMessage;
                if (patch.HubChannelId is not null) c.HubChannelId = ParseId(patch.HubChannelId);
                if (patch.TempChannelNameTemplate is not null) c.TempChannelNameTemplate = patch.TempChannelNameTemplate;
                if (patch.Volume is { } v) c.Volume = Math.Clamp(v, 0, 100);
                if (patch.DjRoleId is not null) c.DjRoleId = ParseId(patch.DjRoleId);
                if (patch.GeminiChatEnabled is { } chat) c.GeminiChatEnabled = chat;
                if (patch.GeminiMentionsEnabled is { } menc) c.GeminiMentionsEnabled = menc;
                if (patch.GeminiSpontaneousEnabled is { } esp) c.GeminiSpontaneousEnabled = esp;
                if (patch.AiWebSearchEnabled is { } busca) c.AiWebSearchEnabled = busca;
                if (patch.AiCommandsEnabled is { } comandos) c.AiCommandsEnabled = comandos;
                if (patch.DownloadsEnabled is { } dl) c.DownloadsEnabled = dl;
                if (patch.Language is { } lang)
                    c.Language = Snowflake.Bot.Utilities.Languages.Normalizar(lang);
            }, ct);

            return Results.Ok(await settings.GetSnapshotAsync(guildId, ct));
        });

        // ---------- Juego de conteo ----------

        group.MapPost("/counting", async (
            ulong guildId,
            CountingPatch patch,
            HttpContext http,
            GuildSettingsService settings,
            CancellationToken ct) =>
        {
            if (!ApiKeyGuard.Autorizado(http)) return Results.Unauthorized();

            await settings.UpdateCountingAsync(guildId, c =>
            {
                if (patch.ChannelId is not null) c.ChannelId = ParseId(patch.ChannelId);
                if (patch.Base is { } nuevaBase) c.Base = nuevaBase;
                if (patch.Goal is not null) c.Goal = patch.Goal;
                if (patch.ExtraChancesPerDay is { } chances) c.ExtraChancesPerDay = Math.Clamp(chances, 0, 10);
                if (patch.EmojiCorrect is not null) c.EmojiCorrect = patch.EmojiCorrect;
                if (patch.EmojiIncorrect is not null) c.EmojiIncorrect = patch.EmojiIncorrect;
                if (patch.EmojiRecord is not null) c.EmojiRecord = patch.EmojiRecord;
                if (patch.LoseMessage is not null) c.LoseMessage = patch.LoseMessage;
            }, ct);

            return Results.Ok(await settings.GetSnapshotAsync(guildId, ct));
        });

        // ---------- YouTube ----------

        group.MapPost("/youtube", async (
            ulong guildId,
            YouTubePatch patch,
            HttpContext http,
            GuildSettingsService settings,
            CancellationToken ct) =>
        {
            if (!ApiKeyGuard.Autorizado(http)) return Results.Unauthorized();

            var sub = await settings.UpdateYouTubeAsync(guildId, s =>
            {
                if (patch.YTChannelId is not null) s.YTChannelId = patch.YTChannelId;
                if (patch.YTChannelName is not null) s.YTChannelName = patch.YTChannelName;
                if (patch.NotifyChannelId is not null) s.NotifyChannelId = ParseId(patch.NotifyChannelId)!.Value;
                if (patch.NotifyRoleId is not null) s.NotifyRoleId = ParseId(patch.NotifyRoleId);
                if (patch.CustomMessage is not null) s.CustomMessage = patch.CustomMessage;
            }, ct);

            return Results.Ok(await settings.GetSnapshotAsync(guildId, ct));
        });

        group.MapDelete("/youtube", async (
            ulong guildId,
            HttpContext http,
            GuildSettingsService settings,
            CancellationToken ct) =>
        {
            if (!ApiKeyGuard.Autorizado(http)) return Results.Unauthorized();
            return await settings.DeleteYouTubeAsync(guildId, ct)
                ? Results.NoContent()
                : Results.NotFound();
        });
    }

    /// <summary>Convierte un id recibido como string (los IDs de Discord no caben en JSON de JS).</summary>
    private static ulong? ParseId(string? s) =>
        ulong.TryParse(s, out var v) ? v : null;

    // ------------------------- Contratos (JSON) -------------------------

    /// <summary>Campos editables de GuildConfig desde el panel. Null = no tocar.</summary>
    public sealed record GuildConfigPatch
    {
        public string? ModLogChannelId { get; init; }
        public string? WelcomeChannelId { get; init; }
        public string? WelcomeMessage { get; init; }
        public string? HubChannelId { get; init; }
        public string? TempChannelNameTemplate { get; init; }
        public int? Volume { get; init; }
        public string? DjRoleId { get; init; }
        public bool? GeminiChatEnabled { get; init; }
        public bool? GeminiMentionsEnabled { get; init; }
        public bool? GeminiSpontaneousEnabled { get; init; }
        public bool? AiWebSearchEnabled { get; init; }
        public bool? AiCommandsEnabled { get; init; }
        public bool? DownloadsEnabled { get; init; }

        /// <summary>Idioma del bot en el servidor: "en", "es" o "pt".</summary>
        public string? Language { get; init; }
    }

    /// <summary>Campos editables del juego de conteo. Null = no tocar.</summary>
    public sealed record CountingPatch
    {
        public string? ChannelId { get; init; }
        public Data.Entities.CountingBase? Base { get; init; }
        public long? Goal { get; init; }
        public int? ExtraChancesPerDay { get; init; }
        public string? EmojiCorrect { get; init; }
        public string? EmojiIncorrect { get; init; }
        public string? EmojiRecord { get; init; }
        public string? LoseMessage { get; init; }
    }

    /// <summary>Campos editables de la suscripción de YouTube. Null = no tocar.</summary>
    public sealed record YouTubePatch
    {
        public string? YTChannelId { get; init; }
        public string? YTChannelName { get; init; }
        public string? NotifyChannelId { get; init; }
        public string? NotifyRoleId { get; init; }
        public string? CustomMessage { get; init; }
    }
}
