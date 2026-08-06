using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Snowflake.Bot.Data;
using Snowflake.Bot.Data.Entities;

namespace Snowflake.Bot.Endpoints;

public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/guilds/{guildId}/config");

        // Obtener configuración general del servidor
        group.MapGet("/", async (ulong guildId, IDbContextFactory<BotDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var config = await db.GuildConfigs.FirstOrDefaultAsync(c => c.GuildId == guildId);
            return config is not null ? Results.Ok(config) : Results.NotFound();
        });

        // Actualizar configuración general
        group.MapPost("/", async (ulong guildId, GuildConfig newConfig, IDbContextFactory<BotDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var config = await db.GuildConfigs.FirstOrDefaultAsync(c => c.GuildId == guildId);
            if (config is null)
            {
                newConfig.GuildId = guildId;
                db.GuildConfigs.Add(newConfig);
            }
            else
            {
                config.ModLogChannelId = newConfig.ModLogChannelId;
                config.WelcomeChannelId = newConfig.WelcomeChannelId;
                config.WelcomeMessage = newConfig.WelcomeMessage;
                config.HubChannelId = newConfig.HubChannelId;
                config.Volume = newConfig.Volume;
                config.GeminiMentionsEnabled = newConfig.GeminiMentionsEnabled;
                config.GeminiSpontaneousEnabled = newConfig.GeminiSpontaneousEnabled;
            }
            await db.SaveChangesAsync();
            return Results.Ok(config ?? newConfig);
        });

        // Obtener configuración de Conteo
        group.MapGet("/counting", async (ulong guildId, IDbContextFactory<BotDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var config = await db.CountingConfigs.FirstOrDefaultAsync(c => c.GuildId == guildId);
            return config is not null ? Results.Ok(config) : Results.NotFound();
        });

        // Actualizar configuración de Conteo (solo los campos editables desde web)
        group.MapPost("/counting", async (ulong guildId, CountingConfig newConfig, IDbContextFactory<BotDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var config = await db.CountingConfigs.FirstOrDefaultAsync(c => c.GuildId == guildId);
            if (config is null)
            {
                newConfig.GuildId = guildId;
                db.CountingConfigs.Add(newConfig);
            }
            else
            {
                config.ChannelId = newConfig.ChannelId;
                config.Base = newConfig.Base;
                config.Goal = newConfig.Goal;
                config.ExtraChancesPerDay = newConfig.ExtraChancesPerDay;
                config.EmojiCorrect = newConfig.EmojiCorrect;
                config.EmojiIncorrect = newConfig.EmojiIncorrect;
                config.EmojiRecord = newConfig.EmojiRecord;
                config.LoseMessage = newConfig.LoseMessage;
            }
            await db.SaveChangesAsync();
            return Results.Ok(config ?? newConfig);
        });

        // Obtener suscripciones de YouTube del servidor
        group.MapGet("/youtube", async (ulong guildId, IDbContextFactory<BotDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var subs = await db.YouTubeSubscriptions.Where(s => s.GuildId == guildId).ToListAsync();
            return Results.Ok(subs);
        });

        // Eliminar una suscripción de YouTube
        group.MapDelete("/youtube/{ytChannelId}", async (ulong guildId, string ytChannelId, IDbContextFactory<BotDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var sub = await db.YouTubeSubscriptions.FirstOrDefaultAsync(s => s.GuildId == guildId && s.YTChannelId == ytChannelId);
            if (sub is null) return Results.NotFound();

            db.YouTubeSubscriptions.Remove(sub);
            await db.SaveChangesAsync();
            return Results.Ok();
        });
    }
}
