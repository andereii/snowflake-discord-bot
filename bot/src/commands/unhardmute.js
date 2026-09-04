import { SlashCommandBuilder, PermissionFlagsBits } from 'discord.js';
import { performUnhardmute } from '../services/hardmuteManager.js';
import MessagesService from '../services/messagesService.js';
import db from '../services/database.js';

export const data = new SlashCommandBuilder()
    .setName('unhardmute')
    .setDescription('Restore roles and permissions after a hardmute')
    .addUserOption(option => 
        option.setName('user')
            .setDescription('User to unhardmute')
            .setRequired(true))
    .addStringOption(option =>
        option.setName('reason')
            .setDescription('Reason')
            .setRequired(false))
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageRoles);

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const usuario = interaction.options.getUser('user');
    const motivo = interaction.options.getString('reason') || MessagesService.get(guildId, 'Moderacion:MotivoPorDefecto');

    try {
        const member = await interaction.guild.members.fetch(usuario.id).catch(() => null);
        if (!member) {
            return interaction.reply({ content: MessagesService.get(guildId, 'Moderacion:Errores:NoEnServidor', { usuario: usuario.username }), ephemeral: true });
        }

        await interaction.deferReply();

        const success = await performUnhardmute(interaction.guild, usuario.id, motivo, interaction.user);

        const row = db.prepare('SELECT Id FROM Incidents WHERE GuildId = ? AND TargetUserId = ? AND Type = ? ORDER BY Id DESC LIMIT 1')
            .get(guildId, usuario.id, 'FinHardmute');

        const exito = MessagesService.get(guildId, 'Moderacion:Exito:FinHardmute', { usuario: usuario.username });
        const formato = MessagesService.get(guildId, 'Moderacion:Exito:Formato', { texto: exito, motivo });
        const caso = MessagesService.get(guildId, 'Moderacion:Caso', { caso: row?.Id || 0 });

        await interaction.editReply({ content: `${formato}\n*${caso}*` });
    } catch (error) {
        console.error('[unhardmute] Error:', error);
        await interaction.editReply({ content: MessagesService.get(guildId, 'Errores:Interno') });
    }
}
