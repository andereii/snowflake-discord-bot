import db from './database.js';
import { registerIncident, announceIncident, IncidentType } from './moderationLog.js';

// Ensure ExpiresAt column exists in HardmuteBackups
try {
    db.prepare('ALTER TABLE HardmuteBackups ADD COLUMN ExpiresAt TEXT').run();
} catch {
    // Column already exists
}

const activeTimers = new Map();

/**
 * Perform unhardmute: restore roles and delete channel overrides
 */
export async function performUnhardmute(guild, userId, reason, moderator = null) {
    const guildId = guild.id;
    const key = `${guildId}_${userId}`;
    if (activeTimers.has(key)) {
        clearTimeout(activeTimers.get(key));
        activeTimers.delete(key);
    }

    const member = await guild.members.fetch(userId).catch(() => null);
    if (!member) {
        db.prepare('DELETE FROM HardmuteBackups WHERE GuildId = ? AND UserId = ?').run(guildId, userId);
        return false;
    }

    // 1. Restore roles
    const backup = db.prepare('SELECT RoleIds FROM HardmuteBackups WHERE GuildId = ? AND UserId = ?').get(guildId, userId);
    if (backup && backup.RoleIds) {
        const roleIds = backup.RoleIds.split(',').map(s => s.trim()).filter(Boolean);
        for (const roleId of roleIds) {
            const rol = guild.roles.cache.get(roleId);
            if (rol && !rol.managed && rol.position < guild.members.me.roles.highest.position) {
                try {
                    await member.roles.add(rol, `Unhardmute: ${reason}`);
                } catch {}
            }
        }
        db.prepare('DELETE FROM HardmuteBackups WHERE GuildId = ? AND UserId = ?').run(guildId, userId);
    }

    // 2. Remove channel permission overwrites
    const channels = await guild.channels.fetch();
    for (const [id, channel] of channels) {
        if (!channel || !channel.permissionOverwrites) continue;
        try {
            const overwrite = channel.permissionOverwrites.cache.get(member.id);
            if (overwrite) {
                await channel.permissionOverwrites.delete(member.id, `Unhardmute: ${reason}`);
            }
        } catch {}
    }

    const modUser = moderator || guild.client.user;
    const incidente = registerIncident(guildId, member.user, modUser, IncidentType.FinHardmute, reason);
    await announceIncident(guild, incidente);

    return true;
}

/**
 * Schedule automatic unhardmute after a duration
 */
export function scheduleUnhardmute(client, guildId, userId, expiresAtIso) {
    const key = `${guildId}_${userId}`;
    if (activeTimers.has(key)) {
        clearTimeout(activeTimers.get(key));
        activeTimers.delete(key);
    }

    const delay = new Date(expiresAtIso).getTime() - Date.now();
    if (delay <= 0) {
        const guild = client.guilds.cache.get(guildId);
        if (guild) {
            performUnhardmute(guild, userId, 'Expiración automática de hardmute');
        }
        return;
    }

    const timer = setTimeout(async () => {
        activeTimers.delete(key);
        const guild = client.guilds.cache.get(guildId);
        if (guild) {
            await performUnhardmute(guild, userId, 'Expiración automática de hardmute');
        }
    }, delay);

    activeTimers.set(key, timer);
}

/**
 * Scan database on startup for active timed hardmutes
 */
export function initHardmuteScheduler(client) {
    try {
        const rows = db.prepare('SELECT GuildId, UserId, ExpiresAt FROM HardmuteBackups WHERE ExpiresAt IS NOT NULL').all();
        for (const row of rows) {
            scheduleUnhardmute(client, String(row.GuildId), String(row.UserId), row.ExpiresAt);
        }
    } catch (err) {
        console.error('[hardmuteManager] Error initializing scheduler:', err);
    }
}

export default {
    performUnhardmute,
    scheduleUnhardmute,
    initHardmuteScheduler
};
