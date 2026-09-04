import { SlashCommandBuilder, PermissionFlagsBits } from 'discord.js';
import { registerIncident, announceIncident, notifyMemberDm, IncidentType } from '../services/moderationLog.js';
import MessagesService from '../services/messagesService.js';
import { parseDuration } from '../services/aiTools.js';

export const data = new SlashCommandBuilder()
    .setName('mute')
    .setDescription('Mute a user (timeout)')
    .addUserOption(option => 
        option.setName('user')
            .setDescription('User to mute')
            .setRequired(true))
    .addStringOption(option =>
        option.setName('duration')
            .setDescription('Duration: 30s, 10m, 2h, 7d (max 28d)')
            .setRequired(true))
    .addStringOption(option =>
        option.setName('reason')
            .setDescription('Reason')
            .setRequired(false))
    .setDefaultMemberPermissions(PermissionFlagsBits.ModerateMembers);

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const usuario = interaction.options.getUser('user');
    const duracionStr = interaction.options.getString('duration');
    const motivo = interaction.options.getString('reason') || MessagesService.get(guildId, 'Moderacion:MotivoPorDefecto');

    if (usuario.id === interaction.user.id) {
        return interaction.reply({ content: MessagesService.get(guildId, 'Moderacion:Errores:MismoUsuario'), ephemeral: true });
    }
    if (usuario.id === interaction.client.user.id) {
        return interaction.reply({ content: MessagesService.get(guildId, 'Moderacion:Errores:AlBot'), ephemeral: true });
    }

    const durationMs = parseDuration(duracionStr);
    if (!durationMs || durationMs <= 0) {
        return interaction.reply({ content: MessagesService.get(guildId, 'Moderacion:Errores:DuracionInvalida'), ephemeral: true });
    }
    if (durationMs > 28 * 86400000) {
        return interaction.reply({ content: MessagesService.get(guildId, 'Moderacion:Errores:DuracionMaxima'), ephemeral: true });
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

        await notifyMemberDm(member, 'Silencio', motivo, { duracion: duracionStr });
        await member.timeout(durationMs, motivo);

        const incidente = registerIncident(guildId, usuario, interaction.user, IncidentType.Silencio, motivo, duracionStr);
        await announceIncident(interaction.guild, incidente);

        const exito = MessagesService.get(guildId, 'Moderacion:Exito:Silencio', { usuario: usuario.username, duracion: duracionStr });
        const formato = MessagesService.get(guildId, 'Moderacion:Exito:Formato', { texto: exito, motivo });
        const caso = MessagesService.get(guildId, 'Moderacion:Caso', { caso: incidente.id });

        await interaction.reply({ content: `${formato}\n*${caso}*` });
    } catch (error) {
        console.error('[mute] Error:', error);
        await interaction.reply({ content: MessagesService.get(guildId, 'Errores:Interno'), ephemeral: true });
    }
}
