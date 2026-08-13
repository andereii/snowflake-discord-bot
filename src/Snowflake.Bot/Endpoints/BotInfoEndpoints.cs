using DSharpPlus;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

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
    }

    /// <summary>Lista de IDs de servidores del usuario (string, no number).</summary>
    public sealed record SharedGuildsRequest(List<string>? GuildIds);
}
