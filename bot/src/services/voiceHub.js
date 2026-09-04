import { ChannelType, PermissionFlagsBits } from 'discord.js';
import db from './database.js';
import MessagesService from './messagesService.js';

/**
 * Handle voice state updates for Join-to-Create system
 */
export async function handleVoiceStateUpdate(oldState, newState) {
    try {
        const guild = newState.guild || oldState.guild;
        if (!guild) return;

        const oldChannelId = oldState?.channelId;
        const newChannelId = newState?.channelId;

        // Skip if channel didn't change (e.g. mute/deafen)
        if (oldChannelId === newChannelId) return;

        // 1) User joined the Hub voice channel -> Create temp voice channel
        if (newChannelId && newState.channel) {
            const config = db.prepare('SELECT CAST(HubChannelId AS TEXT) as HubChannelId, TempChannelNameTemplate FROM GuildConfigs WHERE GuildId = ?').get(guild.id);
            if (config && config.HubChannelId && String(config.HubChannelId) === newChannelId) {
                await createTempVoiceChannel(guild, newState.member, newState.channel, config.TempChannelNameTemplate);
                return;
            }
        }

        // 2) User left a voice channel -> Delete temp channel if empty
        if (oldChannelId && oldState.channel) {
            const temp = db.prepare('SELECT * FROM TempChannels WHERE ChannelId = ?').get(oldChannelId);
            if (temp) {
                const oldChannel = oldState.channel;
                if (oldChannel.members.size === 0) {
                    try {
                        await oldChannel.delete('Canal temporal vacío');
                    } catch (err) {
                        console.warn('[voiceHub] No se pudo borrar el canal temporal:', err.message);
                    }
                    db.prepare('DELETE FROM TempChannels WHERE ChannelId = ?').run(oldChannelId);
                }
            }
        }
    } catch (error) {
        console.error('[voiceHub] Error handling voice state update:', error);
    }
}

/**
 * Create a temporary voice channel for the user and move them into it
 */
async function createTempVoiceChannel(guild, member, hubChannel, nameTemplate) {
    if (!member) return;

    const guildId = guild.id;
    const username = member.user.username;

    const defaultName = MessagesService.get(guildId, 'Voces:NombreTemporal', { usuario: username });
    const channelName = nameTemplate ? nameTemplate.replace('{usuario}', username) : defaultName;

    const parentCategory = hubChannel.parentId;

    const tempChannel = await guild.channels.create({
        name: channelName.slice(0, 100),
        type: ChannelType.GuildVoice,
        parent: parentCategory || undefined,
        permissionOverwrites: [
            {
                id: member.id,
                allow: [
                    PermissionFlagsBits.ManageChannels,
                    PermissionFlagsBits.MoveMembers,
                    PermissionFlagsBits.MuteMembers,
                    PermissionFlagsBits.DeafenMembers,
                    PermissionFlagsBits.Connect,
                    PermissionFlagsBits.Speak,
                    PermissionFlagsBits.ViewChannel
                ]
            }
        ],
        reason: 'join-to-create'
    });

    db.prepare(`
        INSERT INTO TempChannels (ChannelId, GuildId, OwnerUserId, CreatedAt)
        VALUES (?, ?, ?, ?)
    `).run(tempChannel.id, guildId, member.id, new Date().toISOString());

    try {
        await member.voice.setChannel(tempChannel);
    } catch (err) {
        console.warn('[voiceHub] No se pudo mover al usuario al canal temporal:', err.message);
    }
}

export default {
    handleVoiceStateUpdate
};
