import { PermissionFlagsBits } from 'discord.js';
import { slash, userOption, reasonOption } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import { parseDuration, MAX_TIMEOUT_MS } from '../../lib/duration.js';
import { resolveTarget, replyEphemeral, finishAction, notifyMemberDm } from '../../lib/moderation.js';
import { IncidentType } from '../../services/moderationLog.js';
import MessagesService from '../../services/messagesService.js';

function modSlash(name, description, { names, descriptions, permissions, extra } = {}) {
    let data = slash(name, description, {
        names: esPt(names.es, names.pt),
        descriptions: esPt(descriptions.es, descriptions.pt),
        permissions
    });
    data = userOption(data);
    if (extra) data = extra(data);
    return reasonOption(data);
}

async function parseTimeoutDuration(interaction, raw) {
    const ms = parseDuration(raw);
    if (!ms || ms <= 0) {
        await replyEphemeral(interaction, MessagesService.get(interaction.guildId, 'Moderacion:Errores:DuracionInvalida'));
        return null;
    }
    if (ms > MAX_TIMEOUT_MS) {
        await replyEphemeral(interaction, MessagesService.get(interaction.guildId, 'Moderacion:Errores:DuracionMaxima'));
        return null;
    }
    return ms;
}

export const commands = [
    {
        data: modSlash('ban', 'Ban a member from the server', {
            names: { es: 'vetar', pt: 'banir' },
            descriptions: { es: 'Veta a un usuario del servidor', pt: 'Bane um usuário do servidor' },
            permissions: PermissionFlagsBits.BanMembers,
            extra: (data) => data.addIntegerOption(o =>
                o.setName('delete_days').setDescription('Days of messages to delete (0-7)').setMinValue(0).setMaxValue(7)
            )
        }),
        async execute(interaction) {
            const target = await resolveTarget(interaction);
            if (target.error) return replyEphemeral(interaction, target.error);
            const deleteDays = interaction.options.getInteger('delete_days') || 0;
            if (target.member) await notifyMemberDm(target.member, 'Veto', target.reason);
            await interaction.guild.members.ban(target.user.id, {
                deleteMessageSeconds: deleteDays * 24 * 60 * 60,
                reason: target.reason
            });
            await finishAction(interaction, {
                user: target.user, reason: target.reason,
                type: IncidentType.Veto, successKey: 'Moderacion:Exito:Veto'
            });
        }
    },
    {
        data: modSlash('kick', 'Kick a member from the server', {
            names: { es: 'expulsar', pt: 'expulsar' },
            descriptions: { es: 'Expulsa a un usuario del servidor', pt: 'Expulsa um usuário do servidor' },
            permissions: PermissionFlagsBits.KickMembers
        }),
        async execute(interaction) {
            const target = await resolveTarget(interaction, { requireMember: true });
            if (target.error) return replyEphemeral(interaction, target.error);
            await notifyMemberDm(target.member, 'Expulsion', target.reason);
            await target.member.kick(target.reason);
            await finishAction(interaction, {
                user: target.user, reason: target.reason,
                type: IncidentType.Expulsion, successKey: 'Moderacion:Exito:Expulsion'
            });
        }
    },
    {
        data: modSlash('warn', 'Warn a user and log an incident', {
            names: { es: 'advertir', pt: 'avisar' },
            descriptions: { es: 'Advierte a un usuario y registra un incidente', pt: 'Adverte um usuário e registra um incidente' },
            permissions: PermissionFlagsBits.ModerateMembers
        }),
        async execute(interaction) {
            const target = await resolveTarget(interaction);
            if (target.error) return replyEphemeral(interaction, target.error);
            if (target.member) await notifyMemberDm(target.member, 'Advertencia', target.reason);
            await finishAction(interaction, {
                user: target.user, reason: target.reason,
                type: IncidentType.Advertencia, successKey: 'Moderacion:Exito:Advertencia'
            });
        }
    },
    {
        data: modSlash('timeout', 'Timeout (isolate) a user', {
            names: { es: 'aislar', pt: 'isolar' },
            descriptions: { es: 'Aísla a un usuario (timeout)', pt: 'Isola um usuário (timeout)' },
            permissions: PermissionFlagsBits.ModerateMembers,
            extra: (data) => data.addStringOption(o =>
                o.setName('duration').setDescription('Duration: 30s, 10m, 2h, 7d (max 28d)').setRequired(true)
            )
        }),
        async execute(interaction) {
            const durationStr = interaction.options.getString('duration');
            const ms = await parseTimeoutDuration(interaction, durationStr);
            if (!ms) return;
            const target = await resolveTarget(interaction, { requireMember: true });
            if (target.error) return replyEphemeral(interaction, target.error);
            await notifyMemberDm(target.member, 'Aislamiento', target.reason, { duracion: durationStr });
            await target.member.timeout(ms, target.reason);
            await finishAction(interaction, {
                user: target.user, reason: target.reason, duration: durationStr,
                type: IncidentType.Aislamiento, successKey: 'Moderacion:Exito:Aislamiento',
                placeholders: { duracion: durationStr }
            });
        }
    },
    {
        data: modSlash('mute', 'Mute a user (timeout)', {
            names: { es: 'mute', pt: 'mute' },
            descriptions: { es: 'Silencia a un usuario (timeout)', pt: 'Silencia um usuário (timeout)' },
            permissions: PermissionFlagsBits.ModerateMembers,
            extra: (data) => data.addStringOption(o =>
                o.setName('duration').setDescription('Duration: 30s, 10m, 2h, 7d (max 28d)').setRequired(true)
            )
        }),
        async execute(interaction) {
            const durationStr = interaction.options.getString('duration');
            const ms = await parseTimeoutDuration(interaction, durationStr);
            if (!ms) return;
            const target = await resolveTarget(interaction, { requireMember: true });
            if (target.error) return replyEphemeral(interaction, target.error);
            await notifyMemberDm(target.member, 'Silencio', target.reason, { duracion: durationStr });
            await target.member.timeout(ms, target.reason);
            await finishAction(interaction, {
                user: target.user, reason: target.reason, duration: durationStr,
                type: IncidentType.Silencio, successKey: 'Moderacion:Exito:Silencio',
                placeholders: { duracion: durationStr }
            });
        }
    },
    {
        data: modSlash('untimeout', "Remove a user's timeout", {
            names: { es: 'desaislar', pt: 'dessilenciar' },
            descriptions: { es: 'Quita el aislamiento (timeout) a un usuario', pt: 'Remove o silêncio de um usuário' },
            permissions: PermissionFlagsBits.ModerateMembers
        }),
        async execute(interaction) {
            const target = await resolveTarget(interaction, { requireMember: true });
            if (target.error) return replyEphemeral(interaction, target.error);
            await target.member.timeout(null, target.reason);
            await finishAction(interaction, {
                user: target.user, reason: target.reason,
                type: IncidentType.FinAislamiento, successKey: 'Moderacion:Exito:FinAislamiento'
            });
        }
    },
    {
        data: modSlash('softban', 'Ban and immediately unban to delete messages', {
            names: { es: 'softban', pt: 'softban' },
            descriptions: {
                es: 'Banea y desbanea al instante para borrar mensajes',
                pt: 'Bane e desbane instantaneamente para apagar mensagens'
            },
            permissions: PermissionFlagsBits.BanMembers
        }),
        async execute(interaction) {
            const target = await resolveTarget(interaction);
            if (target.error) return replyEphemeral(interaction, target.error);
            if (target.member) await notifyMemberDm(target.member, 'Softban', target.reason);
            await interaction.guild.members.ban(target.user.id, {
                deleteMessageSeconds: 7 * 24 * 60 * 60,
                reason: `Softban: ${target.reason}`
            });
            await interaction.guild.members.unban(target.user.id, 'Softban: automatic unban');
            await finishAction(interaction, {
                user: target.user, reason: target.reason,
                type: IncidentType.Softban, successKey: 'Moderacion:Exito:Softban'
            });
        }
    }
];
