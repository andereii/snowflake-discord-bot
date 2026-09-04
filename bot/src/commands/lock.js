import { SlashCommandBuilder, PermissionFlagsBits } from 'discord.js';
import { lockChannel, isLockableChannel } from '../services/channelLock.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
    .setName('lock')
    .setDescription('Lock a channel: nobody will be able to talk in it (lockdown)')
    .addChannelOption(option =>
        option.setName('channel')
            .setDescription('Channel to lock (empty = this channel)')
            .setRequired(false)
    )
    .addStringOption(option =>
        option.setName('reason')
            .setDescription('Reason for the lockdown')
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

    const applied = await lockChannel(channel, motivo);

    if (applied) {
        await interaction.reply({
            content: MessagesService.get(guildId, 'Bloqueo:Bloqueado', { canal: channel.toString() })
        });
    } else {
        await interaction.reply({
            content: MessagesService.get(guildId, 'Bloqueo:YaBloqueado', { canal: channel.toString() }),
            ephemeral: true
        });
    }
}
