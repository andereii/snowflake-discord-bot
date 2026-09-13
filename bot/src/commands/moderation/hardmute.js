import { PermissionFlagsBits } from 'discord.js';
import { slash, userOption, reasonOption } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import { parseDuration } from '../../lib/duration.js';
import { resolveTarget, replyEphemeral, finishAction, notifyMemberDm } from '../../lib/moderation.js';
import { performHardmute, scheduleUnhardmute } from '../../services/hardmuteManager.js';
import { IncidentType } from '../../services/moderationLog.js';
import MessagesService from '../../services/messagesService.js';

export const data = reasonOption(
    userOption(
        slash('hardmute', 'Strip roles and revoke send/speak permissions in all channels', {
            names: esPt('hardmute'),
            descriptions: esPt(
                'Quita roles y revoca permisos de enviar/hablar en todos los canales',
                'Remove cargos e revoga permissões de enviar/falar em todos os canais'
            ),
            permissions: PermissionFlagsBits.ManageRoles
        })
    ).addStringOption(o =>
        o.setName('duration').setDescription('Duration: 30m, 2h, 7d (empty = indefinite)')
    )
);

export async function execute(interaction) {
    const durationStr = interaction.options.getString('duration');
    let expiresAtIso = null;

    if (durationStr) {
        const ms = parseDuration(durationStr);
        if (!ms || ms <= 0) {
            return replyEphemeral(interaction, MessagesService.get(interaction.guildId, 'Moderacion:Errores:DuracionInvalida'));
        }
        expiresAtIso = new Date(Date.now() + ms).toISOString();
    }

    const target = await resolveTarget(interaction, { requireMember: true });
    if (target.error) return replyEphemeral(interaction, target.error);

    await interaction.deferReply();
    await performHardmute(interaction.guild, target.member, {
        reason: target.reason,
        moderator: interaction.user,
        expiresAtIso
    });

    if (expiresAtIso) scheduleUnhardmute(interaction.client, target.guildId, target.user.id, expiresAtIso);
    await notifyMemberDm(target.member, 'Hardmute', target.reason);

    const success = MessagesService.get(target.guildId, 'Moderacion:Exito:Hardmute', { usuario: target.user.username })
        + (durationStr ? ` (${durationStr})` : '');
    await finishAction(interaction, {
        user: target.user,
        reason: target.reason,
        duration: durationStr,
        type: IncidentType.Hardmute,
        successText: success,
        deferred: true
    });
}
