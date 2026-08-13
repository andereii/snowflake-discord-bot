using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Snowflake.Bot.Data;
using Snowflake.Bot.Data.Entities;

namespace Snowflake.Bot.Services;

/// <summary>
/// Lockdown de canales (/bloquear y /desbloquear): niega a @everyone el envío
/// de mensajes (texto) o la conexión (voz) y guarda el overwrite original para
/// restaurarlo exactamente al desbloquear.
/// </summary>
public sealed class ChannelLockService(
    IDbContextFactory<BotDbContext> dbFactory,
    ILogger<ChannelLockService> logger)
{
    /// <summary>Permisos que se niegan a @everyone al bloquear un canal de texto.</summary>
    public static readonly Permissions BloqueoTexto =
        Permissions.SendMessages | Permissions.AddReactions;

    /// <summary>Permiso que se niega a @everyone al bloquear un canal de voz.</summary>
    public static readonly Permissions BloqueoVoz = Permissions.UseVoice;

    /// <summary>Bloquea el canal (si no lo estaba). Devuelve true si se aplicó ahora.</summary>
    public async Task<bool> BloquearAsync(DiscordChannel canal, string motivo)
    {
        var everyone = canal.Guild.EveryoneRole;
        var bits = BitsSegunTipo(canal);
        if (bits is null) return false;

        await using var db = await dbFactory.CreateDbContextAsync();

        // ¿Ya está bloqueado (por nosotros)? No hacemos nada.
        if (await db.ChannelLocks.FindAsync(canal.Id) is not null)
            return false;

        // Guarda el overwrite actual de @everyone para restaurarlo luego.
        var actual = canal.PermissionOverwrites
            .FirstOrDefault(o => o.Type == OverwriteType.Role && o.Id == everyone.Id);

        var bloqueo = new ChannelLock
        {
            ChannelId = canal.Id,
            GuildId = canal.Guild.Id,
            AllowBits = actual is null ? 0 : (long)actual.Allowed,
            DenyBits = actual is null ? 0 : (long)actual.Denied,
            HadOverwrite = actual is not null
        };
        db.ChannelLocks.Add(bloqueo);

        // Aplica el lockdown preservando los permisos que ya existían.
        var permitidos = actual?.Allowed ?? Permissions.None;
        var denegados = (actual?.Denied ?? Permissions.None) | bits.Value;

        await canal.AddOverwriteAsync(everyone, permitidos, denegados, $"Bloqueo del canal: {motivo}");
        await db.SaveChangesAsync();
        logger.LogInformation("Canal {Canal} bloqueado en {Guild}", canal.Id, canal.Guild.Id);
        return true;
    }

    /// <summary>Desbloquea el canal restaurando el overwrite original. True si se desbloqueó.</summary>
    public async Task<bool> DesbloquearAsync(DiscordChannel canal, string motivo)
    {
        var everyone = canal.Guild.EveryoneRole;

        await using var db = await dbFactory.CreateDbContextAsync();
        var bloqueo = await db.ChannelLocks.FindAsync(canal.Id);
        if (bloqueo is null) return false;

        var bits = BitsSegunTipo(canal);
        var actual = canal.PermissionOverwrites
            .FirstOrDefault(o => o.Type == OverwriteType.Role && o.Id == everyone.Id);

        if (bloqueo.HadOverwrite)
        {
            // Restaura exactamente el overwrite que había antes del bloqueo.
            await canal.AddOverwriteAsync(
                everyone,
                (Permissions)bloqueo.AllowBits,
                (Permissions)bloqueo.DenyBits,
                $"Desbloqueo del canal: {motivo}");
        }
        else if (bits is not null && actual is not null)
        {
            // No había overwrite: quita SOLO los bits del bloqueo del overwrite
            // actual; si queda vacío, elimina el overwrite por completo.
            var permitidos = actual.Allowed & ~bits.Value;
            var denegados = actual.Denied & ~bits.Value;

            if (permitidos == Permissions.None && denegados == Permissions.None)
                await canal.DeleteOverwriteAsync(everyone, $"Desbloqueo del canal: {motivo}");
            else
                await canal.AddOverwriteAsync(everyone, permitidos, denegados, $"Desbloqueo del canal: {motivo}");
        }

        db.ChannelLocks.Remove(bloqueo);
        await db.SaveChangesAsync();
        logger.LogInformation("Canal {Canal} desbloqueado en {Guild}", canal.Id, canal.Guild.Id);
        return true;
    }

    /// <summary>IDs de los canales bloqueados de un servidor (para /config ver y el panel web).</summary>
    public async Task<List<ulong>> ListarAsync(ulong guildId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ChannelLocks
            .Where(l => l.GuildId == guildId)
            .Select(l => l.ChannelId)
            .ToListAsync();
    }

    /// <summary>Bits de bloqueo según el tipo de canal (null = tipo no soportado).</summary>
    private static Permissions? BitsSegunTipo(DiscordChannel canal) => canal.Type switch
    {
        ChannelType.Text or ChannelType.News or ChannelType.GuildForum => BloqueoTexto,
        ChannelType.Voice or ChannelType.Stage => BloqueoVoz,
        _ => null
    };
}
