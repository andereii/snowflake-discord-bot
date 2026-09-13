import { ChannelType } from 'discord.js';
import db from './database.js';
import { registerIncident, announceIncident, IncidentType } from './moderationLog.js';

try {
    db.prepare('ALTER TABLE HardmuteBackups ADD COLUMN ExpiresAt TEXT').run();
} catch {
    // column already exists
}

const MUTE_CHANNEL_TYPES = new Set([
    ChannelType.GuildText,
    ChannelType.GuildVoice,
    ChannelType.GuildForum,
    ChannelType.GuildStageVoice,
    ChannelType.PublicThread,
    ChannelType.PrivateThread
]);

function saveBackup(guildId, userId, roleIds, expiresAtIso) {
    const existing = db.prepare('SELECT Id FROM HardmuteBackups WHERE GuildId = ? AND UserId = ?').get(guildId, userId);
    if (existing) {
        db.prepare('UPDATE HardmuteBackups SET RoleIds = ?, ExpiresAt = ?, CreatedAt = ? WHERE GuildId = ? AND UserId = ?')
            .run(roleIds, expiresAtIso, new Date().toISOString(), guildId, userId);
        return;
    }
    db.prepare('INSERT INTO HardmuteBackups (GuildId, UserId, RoleIds, ExpiresAt, CreatedAt) VALUES (?, ?, ?, ?, ?)')
        .run(guildId, userId, roleIds, expiresAtIso, new Date().toISOString());
}

export async function performHardmute(guild, member, { reason, moderator, expiresAtIso }) {
    const botHighest = guild.members.me.roles.highest.position;
    const removable = member.roles.cache.filter(r =>
        r.id !== guild.roles.everyone.id && !r.managed && r.position < botHighest
    );
    saveBackup(guild.id, member.id, removable.size ? removable.map(r => r.id).join(',') : '', expiresAtIso);

    for (const role of removable.values()) {
        await member.roles.remove(role, `Hardmute by ${moderator.username}`).catch(() => {});
    }

    const channels = await guild.channels.fetch();
    for (const channel of channels.values()) {
        if (!channel || !MUTE_CHANNEL_TYPES.has(channel.type)) continue;
        await channel.permissionOverwrites.edit(member, {
            SendMessages: false,
            Speak: false,
            SendMessagesInThreads: false
        }, { reason: `Hardmute by ${moderator.username}: ${reason}` }).catch(() => {});
    }
}

const activeTimers = new Map();

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

    const backup = db.prepare('SELECT RoleIds FROM HardmuteBackups WHERE GuildId = ? AND UserId = ?').get(guildId, userId);
    if (backup?.RoleIds) {
        const roleIds = backup.RoleIds.split(',').map(s => s.trim()).filter(Boolean);
        for (const roleId of roleIds) {
            const role = guild.roles.cache.get(roleId);
            if (role && !role.managed && role.position < guild.members.me.roles.highest.position) {
                await member.roles.add(role, `Unhardmute: ${reason}`).catch(() => {});
            }
        }
        db.prepare('DELETE FROM HardmuteBackups WHERE GuildId = ? AND UserId = ?').run(guildId, userId);
    }

    const channels = await guild.channels.fetch();
    for (const channel of channels.values()) {
        if (!channel?.permissionOverwrites) continue;
        if (channel.permissionOverwrites.cache.get(member.id)) {
            await channel.permissionOverwrites.delete(member.id, `Unhardmute: ${reason}`).catch(() => {});
        }
    }

    const modUser = moderator || guild.client.user;
    const incident = registerIncident(guildId, member.user, modUser, IncidentType.FinHardmute, reason);
    await announceIncident(guild, incident);
    return true;
}

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
            performUnhardmute(guild, userId, 'Automatic hardmute expiry');
        }
        return;
    }

    const timer = setTimeout(async () => {
        activeTimers.delete(key);
        const guild = client.guilds.cache.get(guildId);
        if (guild) {
            await performUnhardmute(guild, userId, 'Automatic hardmute expiry');
        }
    }, delay);

    activeTimers.set(key, timer);
}

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
    performHardmute,
    performUnhardmute,
    scheduleUnhardmute,
    initHardmuteScheduler
};
