import { PermissionFlagsBits, EmbedBuilder } from 'discord.js';
import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import { updateGuildConfig } from '../../services/guildConfig.js';
import MessagesService from '../../services/messagesService.js';

export const data = slash('modlog', 'Set the channel where moderation incidents are announced', {
    names: esPt('canal-logs', 'canal-de-logs'),
    descriptions: esPt(
        'Establece el canal donde se anuncian los incidentes de moderación',
        'Define o canal onde os incidentes de moderação são anunciados'
    ),
    permissions: PermissionFlagsBits.ManageGuild
}).addChannelOption(o => o.setName('channel').setDescription('Text channel for logs').setRequired(true));

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const channel = interaction.options.getChannel('channel');
    updateGuildConfig(guildId, { ModLogChannelId: channel.id });

    await interaction.reply({
        embeds: [
            new EmbedBuilder()
                .setDescription(MessagesService.get(guildId, 'Config:CanalLogsEstablecido', { canal: channel.toString() }))
                .setColor(0x2ECC71)
        ]
    });
}
