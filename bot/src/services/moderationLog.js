import { EmbedBuilder } from 'discord.js';
import db from './database.js';
import MessagesService from './messagesService.js';

export const IncidentType = {
    Advertencia: 'Advertencia',
    Expulsion: 'Expulsion',
    Veto: 'Veto',
    Aislamiento: 'Aislamiento',
    FinAislamiento: 'FinAislamiento',
    Softban: 'Softban',
    Hardmute: 'Hardmute',
    FinHardmute: 'FinHardmute',
    Silencio: 'Silencio'
};

const IncidentColors = {
    Advertencia: 0xF1C40F, // Yellow
    Expulsion: 0xE67E22,   // Orange
    Veto: 0xE74C3C,        // Red
    Aislamiento: 0x9B59B6, // Purple
    FinAislamiento: 0x2ECC71, // Green
    Softban: 0xE74C3C,     // Red
    Hardmute: 0x8E44AD,    // Dark Purple
    FinHardmute: 0x2ECC71, // Green
    Silencio: 0x9B59B6     // Purple
};

/**
 * Register a moderation incident in the database
 */
export function registerIncident(guildId, targetUser, moderatorUser, type, reason, duration = null) {
    const targetId = targetUser.id;
    const targetTag = targetUser.tag || targetUser.username;
    const moderatorId = moderatorUser.id;
    const moderatorTag = moderatorUser.tag || moderatorUser.username;
    const createdAt = new Date().toISOString();

    const stmt = db.prepare(`
        INSERT INTO Incidents (GuildId, TargetUserId, TargetTag, ModeratorId, ModeratorTag, Type, Reason, Duration, CreatedAt)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
    `);

    const info = stmt.run(guildId, targetId, targetTag, moderatorId, moderatorTag, type, reason, duration, createdAt);

    return {
        id: info.lastInsertRowid,
        guildId,
        targetUserId: targetId,
        targetTag,
        moderatorId,
        moderatorTag,
        type,
        reason,
        duration,
        createdAt
    };
}

/**
 * Announce the incident in the server's configured mod log channel
 */
export async function announceIncident(guild, incident) {
    try {
        const guildConfig = db.prepare('SELECT ModLogChannelId FROM GuildConfigs WHERE GuildId = ?').get(guild.id);
        if (!guildConfig || !guildConfig.ModLogChannelId) return;

        const channel = guild.channels.cache.get(guildConfig.ModLogChannelId)
            || await guild.channels.fetch(guildConfig.ModLogChannelId).catch(() => null);

        if (!channel) return;

        const guildId = guild.id;
        const typeLabel = MessagesService.get(guildId, `Moderacion:Tipos:${incident.type}`);
        const caseLabel = MessagesService.get(guildId, 'Moderacion:Caso', { caso: incident.id });
        const color = IncidentColors[incident.type] || 0x3498DB;

        const embed = new EmbedBuilder()
            .setTitle(`${typeLabel} · ${caseLabel}`)
            .setColor(color)
            .addFields(
                {
                    name: MessagesService.get(guildId, 'Moderacion:Campos:Usuario'),
                    value: `<@${incident.targetUserId}> (${incident.targetTag})`,
                    inline: true
                },
                {
                    name: MessagesService.get(guildId, 'Moderacion:Campos:Moderador'),
                    value: `<@${incident.moderatorId}> (${incident.moderatorTag})`,
                    inline: true
                }
            );

        if (incident.duration) {
            embed.addFields({
                name: MessagesService.get(guildId, 'Moderacion:Campos:Duracion'),
                value: String(incident.duration),
                inline: true
            });
        }

        embed.addFields({
            name: MessagesService.get(guildId, 'Moderacion:Campos:Motivo'),
            value: incident.reason || MessagesService.get(guildId, 'Moderacion:MotivoPorDefecto'),
            inline: false
        });

        embed.setTimestamp(new Date(incident.createdAt));

        await channel.send({ embeds: [embed] });
    } catch (err) {
        console.error('[moderationLog] Error announcing incident:', err);
    }
}

/**
 * Send DM to member about moderation action
 */
export async function notifyMemberDm(member, actionKey, reason, extraPlaceholders = {}) {
    if (!member) return;
    try {
        const guildId = member.guild.id;
        const dmAction = MessagesService.get(guildId, `Moderacion:Dm:Acciones:${actionKey}`, extraPlaceholders);
        const dmTitle = MessagesService.get(guildId, 'Moderacion:Dm:Titulo', {
            accion: dmAction,
            servidor: member.guild.name
        });
        const dmReasonField = MessagesService.get(guildId, 'Moderacion:Dm:CampoMotivo');

        await member.send(`${dmTitle}\n**${dmReasonField}:** ${reason}`).catch(() => {});
    } catch {
        // Ignore DM disabled errors
    }
}

export default {
    IncidentType,
    registerIncident,
    announceIncident,
    notifyMemberDm
};
