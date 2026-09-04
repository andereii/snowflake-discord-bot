using DSharpPlus;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Snowflake.Bot.Services.Settings;

namespace Snowflake.Bot.Endpoints;

/// <summary>
/// Endpoints de información del bot para el panel web.
///
/// Contexto: el navegador, con SOLO el token del usuario, no puede saber en qué
/// servidores está el bot (Discord solo le devuelve los servidores del usuario).
/// Este endpoint cierra ese hueco: el panel envía los IDs de los servidores del
/// usuario y el bot responde en cuáles de ellos está presente.
/// </summary>
public static class BotInfoEndpoints
{
    /// <summary>
    /// POST /api/bot/shared-guilds
    /// Body: { "guildIds": ["123456…", …] } (IDs como string: en JS pierden precisión).
    /// Respuesta: { "shared": [ { "id": "…", "name": "…" }, … ] } — los servidores
    /// del usuario donde el bot está presente.
    /// Nota: si el bot aún no ha terminado de conectar (gateway), devuelve una
    /// lista vacía; el panel puede reintentar unos segundos después.
    /// </summary>
    public static void MapBotInfoEndpoints(this WebApplication app)
    {
        app.MapPost("/api/bot/shared-guilds", async (
            SharedGuildsRequest? req,
            HttpContext http,
            DiscordClient client) =>
        {
            if (!ApiKeyGuard.Autorizado(http)) return Results.Unauthorized();

            var compartidos = new List<object>();
            foreach (var idStr in req?.GuildIds ?? [])
            {
                if (ulong.TryParse(idStr, out var id)
                    && client.Guilds.TryGetValue(id, out var guild))
                {
                    compartidos.Add(new { id = id.ToString(), name = guild.Name });
                }
            }

            return Results.Ok(new { shared = compartidos });
        });

        // GET /api/guilds/{guildId}/stats — estadísticas del servidor para el widget de Inicio
        app.MapGet("/api/guilds/{guildId:long}/stats", async (
            ulong guildId,
            DiscordClient client,
            GuildSettingsService settings) =>
        {
            if (!client.Guilds.TryGetValue(guildId, out var guild))
                return Results.NotFound(new { error = "Servidor no encontrado" });

            var config = await settings.GetAsync(guildId);

            return Results.Ok(new
            {
                guildId = guildId.ToString(),
                name = guild.Name,
                iconUrl = guild.IconUrl,
                memberCount = guild.MemberCount,
                channelCount = guild.Channels.Count,
                roleCount = guild.Roles.Count,
                pollCount = config.PollCount
            });
        });

        // GET /api/guilds/{guildId}/members — lista de miembros (para dropdowns y widget)
        app.MapGet("/api/guilds/{guildId:long}/members", async (
            ulong guildId,
            DiscordClient client) =>
        {
            if (!client.Guilds.TryGetValue(guildId, out var guild))
                return Results.NotFound(new { error = "Servidor no encontrado" });

            var miembros = guild.Members.Values
                .Where(m => !m.IsBot)
                .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(m => new
                {
                    id = m.Id.ToString(),
                    username = m.Username,
                    displayName = m.DisplayName,
                    avatarUrl = m.AvatarUrl
                })
                .ToList();

            return Results.Ok(new { members = miembros });
        });

        // GET /api/guilds/{guildId}/roles — lista de roles (para dropdowns y widget)
        app.MapGet("/api/guilds/{guildId:long}/roles", async (
            ulong guildId,
            DiscordClient client) =>
        {
            if (!client.Guilds.TryGetValue(guildId, out var guild))
                return Results.NotFound(new { error = "Servidor no encontrado" });

            var roles = guild.Roles.Values
                .Where(r => r.Name != "@everyone")
                .OrderByDescending(r => r.Position)
                .Select(r => new
                {
                    id = r.Id.ToString(),
                    name = r.Name,
                    color = r.Color.Value.ToString("X6")
                })
                .ToList();

            return Results.Ok(new { roles = roles });
        });

        // GET /api/guilds/{guildId}/emojis — emojis custom del servidor (para el generador de embeds)
        app.MapGet("/api/guilds/{guildId:long}/emojis", async (
            ulong guildId,
            DiscordClient client) =>
        {
            if (!client.Guilds.TryGetValue(guildId, out var guild))
                return Results.NotFound(new { error = "Servidor no encontrado" });

            var emojis = guild.Emojis.Values
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new
                {
                    id = e.Id.ToString(),
                    name = e.Name,
                    url = e.Url,
                    animated = e.IsAnimated
                })
                .ToList();

            return Results.Ok(new { emojis = emojis });
        });

        // GET /api/guilds/{guildId}/channels — canales del servidor (para el generador de embeds)
        app.MapGet("/api/guilds/{guildId:long}/channels", async (
            ulong guildId,
            DiscordClient client) =>
        {
            if (!client.Guilds.TryGetValue(guildId, out var guild))
                return Results.NotFound(new { error = "Servidor no encontrado" });

            var canales = guild.Channels.Values
                .Where(c => c.Type == DSharpPlus.ChannelType.Text || c.Type == DSharpPlus.ChannelType.News)
                .OrderBy(c => c.Position)
                .Select(c => new
                {
                    id = c.Id.ToString(),
                    name = c.Name,
                    type = c.Type.ToString()
                })
                .ToList();

            return Results.Ok(new { channels = canales });
        });
    }

    /// <summary>Lista de IDs de servidores del usuario (string, no number).</summary>
    public sealed record SharedGuildsRequest(List<string>? GuildIds);
}
