import { ChannelType, PermissionFlagsBits, OverwriteType } from 'discord.js';
import db from './database.js';

export const TextLockDeny = [
    PermissionFlagsBits.SendMessages,
    PermissionFlagsBits.SendMessagesInThreads,
    PermissionFlagsBits.AddReactions,
    PermissionFlagsBits.CreatePublicThreads,
    PermissionFlagsBits.CreatePrivateThreads
];

export const VoiceLockDeny = [
    PermissionFlagsBits.Connect,
    PermissionFlagsBits.Speak
];

export function isLockableChannel(channel) {
    if (!channel) return false;
    return (
        channel.type === ChannelType.GuildText ||
        channel.type === ChannelType.GuildVoice ||
        channel.type === ChannelType.GuildAnnouncement ||
        channel.type === ChannelType.GuildForum ||
        channel.type === ChannelType.GuildStageVoice
    );
}

export function isVoiceType(channel) {
    return channel.type === ChannelType.GuildVoice || channel.type === ChannelType.GuildStageVoice;
}

/**
 * Lock a channel: deny @everyone from speaking/sending messages and save previous permissions
 */
export async function lockChannel(channel, reason = 'Lockdown') {
    if (!isLockableChannel(channel)) return false;

    const guildId = channel.guild.id;
    const channelId = channel.id;

    const existing = db.prepare('SELECT ChannelId FROM ChannelLocks WHERE ChannelId = ?').get(channelId);
    if (existing) return false;

    const everyoneRole = channel.guild.roles.everyone;
    const overwrite = channel.permissionOverwrites.cache.get(everyoneRole.id);

    const allowBits = overwrite ? overwrite.allow.bitfield.toString() : '0';
    const denyBits = overwrite ? overwrite.deny.bitfield.toString() : '0';
    const hadOverwrite = overwrite ? 1 : 0;
    const lockedAt = new Date().toISOString();

    db.prepare(`
        INSERT INTO ChannelLocks (ChannelId, GuildId, AllowBits, DenyBits, HadOverwrite, LockedAt)
        VALUES (?, ?, ?, ?, ?, ?)
    `).run(channelId, guildId, allowBits, denyBits, hadOverwrite, lockedAt);

    const isVoice = isVoiceType(channel);
    const denies = isVoice ? { Connect: false, Speak: false } : {
        SendMessages: false,
        SendMessagesInThreads: false,
        AddReactions: false,
        CreatePublicThreads: false,
        CreatePrivateThreads: false
    };

    await channel.permissionOverwrites.edit(everyoneRole, denies, {
        reason: `Bloqueo del canal: ${reason}`
    });

    return true;
}

/**
 * Unlock a channel: restore original permissions from database
 */
export async function unlockChannel(channel, reason = 'Desbloqueo') {
    if (!isLockableChannel(channel)) return false;

    const channelId = channel.id;
    const row = db.prepare('SELECT * FROM ChannelLocks WHERE ChannelId = ?').get(channelId);
    if (!row) return false;

    const everyoneRole = channel.guild.roles.everyone;

    if (row.HadOverwrite === 1) {
        // Restaura el bitfield exacto previo
        await channel.permissionOverwrites.set([
            ...channel.permissionOverwrites.cache.filter(o => o.id !== everyoneRole.id).values(),
            {
                id: everyoneRole.id,
                type: OverwriteType.Role,
                allow: BigInt(row.AllowBits || 0),
                deny: BigInt(row.DenyBits || 0)
            }
        ], `Desbloqueo del canal: ${reason}`);
    } else {
        // No tenía overwrite previo: elimina el overwrite de @everyone si existe
        const overwrite = channel.permissionOverwrites.cache.get(everyoneRole.id);
        if (overwrite) {
            await overwrite.delete(`Desbloqueo del canal: ${reason}`).catch(() => {});
        }
    }

    db.prepare('DELETE FROM ChannelLocks WHERE ChannelId = ?').run(channelId);
    return true;
}

export default {
    isLockableChannel,
    isVoiceType,
    lockChannel,
    unlockChannel
};
