import { SlashCommandBuilder, PermissionFlagsBits } from 'discord.js';
import { registerIncident, announceIncident, notifyMemberDm, IncidentType } from '../services/moderationLog.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
    .setName('warn')
    .setDescription('Warn a user and log an incident')
    .addUserOption(option => 
        option.setName('user')
            .setDescription('User to warn')
            .setRequired(true))
    .addStringOption(option =>
        option.setName('reason')
            .setDescription('Reason for the warning')
            .setRequired(false))
    .setDefaultMemberPermissions(PermissionFlagsBits.ModerateMembers);

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const usuario = interaction.options.getUser('user');
    const motivo = interaction.options.getString('reason') || MessagesService.get(guildId, 'Moderacion:MotivoPorDefecto');

    if (usuario.id === interaction.user.id) {
        return interaction.reply({ content: MessagesService.get(guildId, 'Moderacion:Errores:MismoUsuario'), ephemeral: true });
    }
    if (usuario.id === interaction.client.user.id) {
        return interaction.reply({ content: MessagesService.get(guildId, 'Moderacion:Errores:AlBot'), ephemeral: true });
    }

    try {
        const member = await interaction.guild.members.fetch(usuario.id).catch(() => null);
        if (member) {
            await notifyMemberDm(member, 'Advertencia', motivo);
        }

        const incidente = registerIncident(guildId, usuario, interaction.user, IncidentType.Advertencia, motivo);
        await announceIncident(interaction.guild, incidente);

        const exito = MessagesService.get(guildId, 'Moderacion:Exito:Advertencia', { usuario: usuario.username });
        const formato = MessagesService.get(guildId, 'Moderacion:Exito:Formato', { texto: exito, motivo });
        const caso = MessagesService.get(guildId, 'Moderacion:Caso', { caso: incidente.id });

        await interaction.reply({ content: `${formato}\n*${caso}*` });
    } catch (error) {
        console.error('[warn] Error:', error);
        await interaction.reply({ content: MessagesService.get(guildId, 'Errores:Interno'), ephemeral: true });
    }
}
