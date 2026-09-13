import { PermissionFlagsBits } from 'discord.js';
import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import { lockChannel, unlockChannel, isLockableChannel } from '../../services/channelLock.js';
import { defaultReason } from '../../lib/moderation.js';
import MessagesService from '../../services/messagesService.js';

function channelOptions(builder) {
    return builder
        .addChannelOption(o => o.setName('channel').setDescription('Channel (empty = this channel)'))
        .addStringOption(o => o.setName('reason').setDescription('Reason'));
}

async function assertLockPermissions(interaction, channel) {
    const guildId = interaction.guildId;
    if (!isLockableChannel(channel)) {
        await interaction.reply({ content: MessagesService.get(guildId, 'Bloqueo:CanalInvalido'), ephemeral: true });
        return false;
    }
    const memberPerms = channel.permissionsFor(interaction.member);
    if (!memberPerms?.has(PermissionFlagsBits.ManageChannels)) {
        await interaction.reply({
            content: MessagesService.get(guildId, 'Bloqueo:SinPermisosCanal', { canal: channel.toString() }),
            ephemeral: true
        });
        return false;
    }
    const botPerms = channel.permissionsFor(interaction.guild.members.me);
    if (!botPerms?.has(PermissionFlagsBits.ManageRoles)) {
        await interaction.reply({
            content: MessagesService.get(guildId, 'Bloqueo:SinPermisosBotCanal', { canal: channel.toString() }),
            ephemeral: true
        });
        return false;
    }
    return true;
}

export const commands = [
    {
        data: channelOptions(slash('lock', 'Lock a channel so nobody can talk in it', {
            names: esPt('bloquear'),
            descriptions: esPt(
                'Bloquea un canal: nadie podrá hablar en él (lockdown)',
                'Bloqueia um canal: ninguém poderá falar nele (lockdown)'
            ),
            permissions: PermissionFlagsBits.ManageChannels
        })),
        async execute(interaction) {
            const guildId = interaction.guildId;
            const channel = interaction.options.getChannel('channel') || interaction.channel;
            const reason = interaction.options.getString('reason') || defaultReason(guildId);
            if (!await assertLockPermissions(interaction, channel)) return;

            const applied = await lockChannel(channel, reason);
            await interaction.reply({
                content: MessagesService.get(guildId, applied ? 'Bloqueo:Bloqueado' : 'Bloqueo:YaBloqueado', {
                    canal: channel.toString()
                }),
                ephemeral: !applied
            });
        }
    },
    {
        data: channelOptions(slash('unlock', 'Unlock a channel and restore previous permissions', {
            names: esPt('desbloquear'),
            descriptions: esPt(
                'Desbloquea un canal: restaura los permisos anteriores',
                'Desbloqueia um canal: restaura as permissões anteriores'
            ),
            permissions: PermissionFlagsBits.ManageChannels
        })),
        async execute(interaction) {
            const guildId = interaction.guildId;
            const channel = interaction.options.getChannel('channel') || interaction.channel;
            const reason = interaction.options.getString('reason') || defaultReason(guildId);
            if (!await assertLockPermissions(interaction, channel)) return;

            const applied = await unlockChannel(channel, reason);
            await interaction.reply({
                content: MessagesService.get(guildId, applied ? 'Bloqueo:Desbloqueado' : 'Bloqueo:NoBloqueado', {
                    canal: channel.toString()
                }),
                ephemeral: !applied
            });
        }
    }
];
