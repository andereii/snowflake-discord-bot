import { SlashCommandBuilder, PermissionFlagsBits } from 'discord.js';
import { registerIncident, announceIncident, notifyMemberDm, IncidentType } from '../services/moderationLog.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
    .setName('softban')
    .setDescription('Ban and immediately unban to delete messages')
    .addUserOption(option => 
        option.setName('user')
            .setDescription('User to softban')
            .setRequired(true))
    .addStringOption(option =>
        option.setName('reason')
            .setDescription('Reason for the softban')
            .setRequired(false))
    .setDefaultMemberPermissions(PermissionFlagsBits.BanMembers);

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
            if (interaction.guild.ownerId !== interaction.user.id && member.roles.highest.position >= interaction.member.roles.highest.position) {
                return interaction.reply({ content: MessagesService.get(guildId, 'Moderacion:Errores:Jerarquia', { usuario: usuario.username }), ephemeral: true });
            }
            if (member.roles.highest.position >= interaction.guild.members.me.roles.highest.position) {
                return interaction.reply({ content: MessagesService.get(guildId, 'Moderacion:Errores:Jerarquia', { usuario: usuario.username }), ephemeral: true });
            }

            await notifyMemberDm(member, 'Softban', motivo);
        }

        await interaction.guild.members.ban(usuario.id, { 
            deleteMessageSeconds: 7 * 24 * 60 * 60, 
            reason: `Softban: ${motivo}` 
        });
        await interaction.guild.members.unban(usuario.id, 'Softban: unban automático');

        const incidente = registerIncident(guildId, usuario, interaction.user, IncidentType.Softban, motivo);
        await announceIncident(interaction.guild, incidente);

        const exito = MessagesService.get(guildId, 'Moderacion:Exito:Softban', { usuario: usuario.username });
        const formato = MessagesService.get(guildId, 'Moderacion:Exito:Formato', { texto: exito, motivo });
        const caso = MessagesService.get(guildId, 'Moderacion:Caso', { caso: incidente.id });

        await interaction.reply({ content: `${formato}\n*${caso}*` });
    } catch (error) {
        console.error('[softban] Error:', error);
        await interaction.reply({ content: MessagesService.get(guildId, 'Errores:Interno'), ephemeral: true });
    }
}
