import { SlashCommandBuilder, PermissionFlagsBits } from 'discord.js';
import { registerIncident, announceIncident, IncidentType } from '../services/moderationLog.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
    .setName('untimeout')
    .setDescription('Remove a user\'s timeout')
    .addUserOption(option => 
        option.setName('user')
            .setDescription('User to remove timeout from')
            .setRequired(true))
    .addStringOption(option =>
        option.setName('reason')
            .setDescription('Reason')
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
        if (!member) {
            return interaction.reply({ content: MessagesService.get(guildId, 'Moderacion:Errores:NoEnServidor', { usuario: usuario.username }), ephemeral: true });
        }

        if (interaction.guild.ownerId !== interaction.user.id && member.roles.highest.position >= interaction.member.roles.highest.position) {
            return interaction.reply({ content: MessagesService.get(guildId, 'Moderacion:Errores:Jerarquia', { usuario: usuario.username }), ephemeral: true });
        }
        if (member.roles.highest.position >= interaction.guild.members.me.roles.highest.position) {
            return interaction.reply({ content: MessagesService.get(guildId, 'Moderacion:Errores:Jerarquia', { usuario: usuario.username }), ephemeral: true });
        }

        await member.timeout(null, motivo);

        const incidente = registerIncident(guildId, usuario, interaction.user, IncidentType.FinAislamiento, motivo);
        await announceIncident(interaction.guild, incidente);

        const exito = MessagesService.get(guildId, 'Moderacion:Exito:FinAislamiento', { usuario: usuario.username });
        const formato = MessagesService.get(guildId, 'Moderacion:Exito:Formato', { texto: exito, motivo });
        const caso = MessagesService.get(guildId, 'Moderacion:Caso', { caso: incidente.id });

        await interaction.reply({ content: `${formato}\n*${caso}*` });
    } catch (error) {
        console.error('[untimeout] Error:', error);
        await interaction.reply({ content: MessagesService.get(guildId, 'Errores:Interno'), ephemeral: true });
    }
}
