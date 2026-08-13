using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Snowflake.Bot.Data;
using Snowflake.Bot.Data.Entities;
using Snowflake.Bot.Services.Settings;

namespace Snowflake.Bot.Services;

/// <summary>
/// Sistema "join-to-create": al entrar al canal hub configurado, se crea un
/// canal de voz temporal propiedad del usuario; cuando queda vacío, se borra.
/// </summary>
public sealed class VoiceHubService(
    IDbContextFactory<BotDbContext> dbFactory,
    GuildSettingsService settings,
    MessagesService msg,
    ILogger<VoiceHubService> logger)
{
    /// <summary>Hook de evento de cambio de estado de voz (gestiona todo aquí).</summary>
    public async Task OnVoiceStateUpdatedAsync(DiscordClient sender, VoiceStateUpdateEventArgs e)
    {
        try
        {
            var guild = e.Guild;
            if (guild is null) return;

            // Ignoramos mute/unmute/deafen: solo cambios de canal.
            var antes = e.Before?.Channel?.Id;
            var ahora = e.After?.Channel?.Id;
            if (antes == ahora) return;

            // 1) Si acaba de entrar al hub, se le crea un canal temporal.
            if (ahora is not null && e.After?.Channel is { } canalHub)
            {
                var config = await settings.GetAsync(guild.Id);
                if (config.HubChannelId is ulong hubId && ahora == hubId)
                {
                    await CrearCanalTemporalAsync(guild, e.User ?? e.After.User, canalHub, config.TempChannelNameTemplate);
                    return;
                }
            }

            // 2) El canal que acaba de dejar: si es temporal y está vacío, se borra.
            if (e.Before?.Channel is { } canalPrevio)
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var temp = await db.TempChannels.FindAsync(canalPrevio.Id);
                if (temp is null) return;

                if (canalPrevio.Users.Count == 0)
                {
                    try { await canalPrevio.DeleteAsync("Canal temporal vacío"); }
                    catch (Exception ex) { logger.LogWarning(ex, "No se pudo borrar el canal temporal {Id}", canalPrevio.Id); }

                    db.TempChannels.Remove(temp);
                    await db.SaveChangesAsync();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error en el sistema join-to-create");
        }
    }

    private async Task CrearCanalTemporalAsync(
        DiscordGuild guild, DiscordUser usuario, DiscordChannel hub, string? plantilla)
    {
        var member = await guild.GetMemberAsync(usuario.Id);

        // Plantilla personalizada del servidor o el nombre por defecto del bot.
        var nombre = string.IsNullOrWhiteSpace(plantilla)
            ? msg.Get("Voces:NombreTemporal", ("usuario", usuario.Username))
            : plantilla.Replace("{usuario}", usuario.Username);

        var categoria = hub.Parent;

        var overwrites = new[]
        {
            new DiscordOverwriteBuilder(member)
                .Allow(Permissions.ManageChannels
                       | Permissions.MoveMembers
                       | Permissions.MuteMembers
                       | Permissions.DeafenMembers
                       | Permissions.AccessChannels
                       | Permissions.UseVoice)
        };

        DiscordChannel temp;
        if (categoria is not null)
            temp = await guild.CreateVoiceChannelAsync(nombre, categoria, overwrites: overwrites, reason: "join-to-create");
        else
            temp = await guild.CreateVoiceChannelAsync(nombre, overwrites: overwrites, reason: "join-to-create");

        await using var db = await dbFactory.CreateDbContextAsync();
        db.TempChannels.Add(new TempChannel
        {
            ChannelId = temp.Id,
            GuildId = guild.Id,
            OwnerUserId = usuario.Id
        });
        await db.SaveChangesAsync();

        await member.ModifyAsync(m => m.VoiceChannel = temp);
        logger.LogInformation("Canal temporal {Canal} creado para {Usuario} en {Guild}", temp.Id, usuario.Id, guild.Id);
    }
}