import { EmbedBuilder, PermissionFlagsBits } from 'discord.js';
import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import { getGuildConfig, updateGuildConfig } from '../../services/guildConfig.js';
import MessagesService from '../../services/messagesService.js';

export const data = slash('welcome', 'Configure welcome messages', {
    names: esPt('bienvenida', 'boas-vindas'),
    descriptions: esPt('Configura los mensajes de bienvenida', 'Configura as mensagens de boas-vindas'),
    permissions: PermissionFlagsBits.ManageGuild
})
    .addSubcommand(sub => sub.setName('channel').setDescription('Set the welcome channel')
        .addChannelOption(o => o.setName('channel').setDescription('Text channel for welcomes').setRequired(true)))
    .addSubcommand(sub => sub.setName('message').setDescription('Set custom welcome message ({user} {server})')
        .addStringOption(o => o.setName('text').setDescription('Welcome message. Max 1900 chars.').setRequired(true)))
    .addSubcommand(sub => sub.setName('view').setDescription('Show current welcome configuration'))
    .addSubcommand(sub => sub.setName('disable').setDescription('Disable welcome messages'));

export async function execute(interaction) {
    const guildId = interaction.guild.id;
    const sub = interaction.options.getSubcommand();

    if (sub === 'channel') {
        const channel = interaction.options.getChannel('channel');
        updateGuildConfig(guildId, { WelcomeChannelId: channel.id });
        return interaction.reply({
            content: MessagesService.get(guildId, 'Bienvenida:ConfigCanalExito', { canal: channel.toString() }),
            ephemeral: true
        });
    }

    if (sub === 'message') {
        const text = interaction.options.getString('text');
        if (text.length > 1900) {
            return interaction.reply({ content: MessagesService.get(guildId, 'Bienvenida:MensajeLargo'), ephemeral: true });
        }
        updateGuildConfig(guildId, { WelcomeMessage: text });
        const preview = text
            .replace(/{usuario}|{user}/g, interaction.user.toString())
            .replace(/{servidor}|{server}/g, interaction.guild.name);
        return interaction.reply({
            content: MessagesService.get(guildId, 'Bienvenida:ConfigMensajeExito', { vista: preview }),
            ephemeral: true
        });
    }

    if (sub === 'view') {
        const row = getGuildConfig(guildId);
        const channelStr = row?.WelcomeChannelId
            ? `<#${row.WelcomeChannelId}>`
            : MessagesService.get(guildId, 'Bienvenida:VerNoConfigurado');
        const messageStr = row?.WelcomeMessage
            || `${MessagesService.get(guildId, 'Bienvenida:MensajePorDefecto', { usuario: '{usuario}', servidor: interaction.guild.name })}\n${MessagesService.get(guildId, 'Bienvenida:VerPorDefecto')}`;

        return interaction.reply({
            embeds: [
                new EmbedBuilder()
                    .setTitle(MessagesService.get(guildId, 'Bienvenida:VerTitulo'))
                    .setColor(0x0099FF)
                    .addFields(
                        { name: MessagesService.get(guildId, 'Bienvenida:VerCanal'), value: channelStr, inline: true },
                        { name: MessagesService.get(guildId, 'Bienvenida:VerMensaje'), value: messageStr, inline: false }
                    )
            ],
            ephemeral: true
        });
    }

    if (sub === 'disable') {
        const row = getGuildConfig(guildId);
        if (!row?.WelcomeChannelId) {
            return interaction.reply({ content: MessagesService.get(guildId, 'Bienvenida:YaDesactivada'), ephemeral: true });
        }
        updateGuildConfig(guildId, { WelcomeChannelId: null });
        return interaction.reply({ content: MessagesService.get(guildId, 'Bienvenida:ConfigDesactivada'), ephemeral: true });
    }
}
