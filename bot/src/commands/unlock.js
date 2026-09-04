import { SlashCommandBuilder, PermissionFlagsBits } from 'discord.js';
import { unlockChannel, isLockableChannel } from '../services/channelLock.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
    .setName('unlock')
    .setDescription('Unlock a channel: restore the previous permissions')
    .addChannelOption(option =>
        option.setName('channel')
            .setDescription('Channel to unlock (empty = this channel)')
            .setRequired(false)
    )
    .addStringOption(option =>
        option.setName('reason')
            .setDescription('Reason for the unlock')
            .setRequired(false)
    )
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageChannels);

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const channel = interaction.options.getChannel('channel') || interaction.channel;
    const motivo = interaction.options.getString('reason') || MessagesService.get(guildId, 'Moderacion:MotivoPorDefecto');

    if (!isLockableChannel(channel)) {
        return interaction.reply({
            content: MessagesService.get(guildId, 'Bloqueo:CanalInvalido'),
            ephemeral: true
        });
    }

    const memberPermissions = channel.permissionsFor(interaction.member);
    if (!memberPermissions || !memberPermissions.has(PermissionFlagsBits.ManageChannels)) {
        return interaction.reply({
            content: MessagesService.get(guildId, 'Bloqueo:SinPermisosCanal', { canal: channel.toString() }),
            ephemeral: true
        });
    }

    const botPermissions = channel.permissionsFor(interaction.guild.members.me);
    if (!botPermissions || !botPermissions.has(PermissionFlagsBits.ManageRoles)) {
        return interaction.reply({
            content: MessagesService.get(guildId, 'Bloqueo:SinPermisosBotCanal', { canal: channel.toString() }),
            ephemeral: true
        });
    }

    const applied = await unlockChannel(channel, motivo);

    if (applied) {
        await interaction.reply({
            content: MessagesService.get(guildId, 'Bloqueo:Desbloqueado', { canal: channel.toString() })
        });
    } else {
        await interaction.reply({
            content: MessagesService.get(guildId, 'Bloqueo:NoBloqueado', { canal: channel.toString() }),
            ephemeral: true
        });
    }
}
