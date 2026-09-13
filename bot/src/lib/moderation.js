import MessagesService from '../services/messagesService.js';
import { registerIncident, announceIncident, notifyMemberDm } from '../services/moderationLog.js';

export function defaultReason(guildId) {
    return MessagesService.get(guildId, 'Moderacion:MotivoPorDefecto');
}

export function hierarchyError(interaction, member) {
    const guildId = interaction.guildId;
    const username = member.user?.username || member.displayName;
    if (interaction.guild.ownerId !== interaction.user.id
        && member.roles.highest.position >= interaction.member.roles.highest.position) {
        return MessagesService.get(guildId, 'Moderacion:Errores:Jerarquia', { usuario: username });
    }
    if (member.roles.highest.position >= interaction.guild.members.me.roles.highest.position) {
        return MessagesService.get(guildId, 'Moderacion:Errores:Jerarquia', { usuario: username });
    }
    return null;
}

export async function resolveTarget(interaction, { requireMember = false } = {}) {
    const guildId = interaction.guildId;
    const user = interaction.options.getUser('user');
    const reason = interaction.options.getString('reason') || defaultReason(guildId);

    if (user.id === interaction.user.id) {
        return { error: MessagesService.get(guildId, 'Moderacion:Errores:MismoUsuario') };
    }
    if (user.id === interaction.client.user.id) {
        return { error: MessagesService.get(guildId, 'Moderacion:Errores:AlBot') };
    }

    const member = await interaction.guild.members.fetch(user.id).catch(() => null);
    if (requireMember && !member) {
        return { error: MessagesService.get(guildId, 'Moderacion:Errores:NoEnServidor', { usuario: user.username }) };
    }
    if (member) {
        const err = hierarchyError(interaction, member);
        if (err) return { error: err };
    }

    return { guildId, user, member, reason };
}

export async function replyEphemeral(interaction, content) {
    return interaction.reply({ content, ephemeral: true });
}

export async function finishAction(interaction, {
    user,
    reason,
    type,
    successKey,
    successText,
    placeholders = {},
    duration = null,
    deferred = false
}) {
    const incident = registerIncident(interaction.guildId, user, interaction.user, type, reason, duration);
    await announceIncident(interaction.guild, incident);

    const success = successText || MessagesService.get(interaction.guildId, successKey, {
        usuario: user.username,
        ...placeholders
    });
    const formatted = MessagesService.get(interaction.guildId, 'Moderacion:Exito:Formato', {
        texto: success,
        motivo: reason
    });
    const caseLine = MessagesService.get(interaction.guildId, 'Moderacion:Caso', { caso: incident.id });
    const content = `${formatted}\n*${caseLine}*`;

    if (deferred) return interaction.editReply({ content });
    return interaction.reply({ content });
}

export { notifyMemberDm };
