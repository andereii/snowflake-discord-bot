import { PermissionFlagsBits } from 'discord.js';
import { slash, userOption, reasonOption } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import { defaultReason } from '../../lib/moderation.js';
import { performUnhardmute } from '../../services/hardmuteManager.js';
import MessagesService from '../../services/messagesService.js';
import db from '../../services/database.js';

export const data = reasonOption(userOption(
    slash('unhardmute', 'Restore roles and permissions after a hardmute', {
        names: esPt('unhardmute'),
        descriptions: esPt(
            'Restaura roles y permisos tras un hardmute',
            'Restaura cargos e permissões após um hardmute'
        ),
        permissions: PermissionFlagsBits.ManageRoles
    })
));

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const user = interaction.options.getUser('user');
    const reason = interaction.options.getString('reason') || defaultReason(guildId);

    const member = await interaction.guild.members.fetch(user.id).catch(() => null);
    if (!member) {
        return interaction.reply({
            content: MessagesService.get(guildId, 'Moderacion:Errores:NoEnServidor', { usuario: user.username }),
            ephemeral: true
        });
    }

    await interaction.deferReply();
    try {
        await performUnhardmute(interaction.guild, user.id, reason, interaction.user);
        const row = db.prepare(
            'SELECT Id FROM Incidents WHERE GuildId = ? AND TargetUserId = ? AND Type = ? ORDER BY Id DESC LIMIT 1'
        ).get(guildId, user.id, 'FinHardmute');

        const success = MessagesService.get(guildId, 'Moderacion:Exito:FinHardmute', { usuario: user.username });
        const formatted = MessagesService.get(guildId, 'Moderacion:Exito:Formato', { texto: success, motivo: reason });
        const caseLine = MessagesService.get(guildId, 'Moderacion:Caso', { caso: row?.Id || 0 });
        await interaction.editReply({ content: `${formatted}\n*${caseLine}*` });
    } catch (error) {
        console.error('[unhardmute]', error);
        await interaction.editReply({ content: MessagesService.get(guildId, 'Errores:Interno') });
    }
}
