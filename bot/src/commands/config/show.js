import { EmbedBuilder } from 'discord.js';
import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import { getGuildConfig, isEnabled } from '../../services/guildConfig.js';
import MessagesService from '../../services/messagesService.js';
import db from '../../services/database.js';

const LANGUAGE_NAMES = { es: 'Español', pt: 'Português', en: 'English' };
const mark = (on) => (on ? '✅' : '❌');

export const data = slash('show', 'Show the summary of all bot settings on this server', {
    names: esPt('ver'),
    descriptions: esPt(
        'Muestra el resumen de ajustes del bot en este servidor',
        'Mostra o resumo de ajustes do bot neste servidor'
    )
});

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const cfg = getGuildConfig(guildId) || {};

    const embed = new EmbedBuilder()
        .setTitle(MessagesService.get(guildId, 'Config:VerTitulo', { servidor: interaction.guild.name }))
        .setColor(0x3498DB)
        .addFields(
            {
                name: MessagesService.get(guildId, 'Config:VerModeracion'),
                value: cfg.ModLogChannelId ? `<#${cfg.ModLogChannelId}>` : MessagesService.get(guildId, 'Config:VerNoConfigurado'),
                inline: true
            },
            {
                name: MessagesService.get(guildId, 'Config:VerBienvenida'),
                value: cfg.WelcomeChannelId ? `<#${cfg.WelcomeChannelId}>` : MessagesService.get(guildId, 'Config:VerDesactivado'),
                inline: true
            },
            {
                name: MessagesService.get(guildId, 'Config:VerVoces'),
                value: cfg.HubChannelId ? `<#${cfg.HubChannelId}>` : MessagesService.get(guildId, 'Config:VerDesactivado'),
                inline: true
            },
            {
                name: MessagesService.get(guildId, 'Config:VerMusica'),
                value: cfg.DjRoleId
                    ? MessagesService.get(guildId, 'Config:VerDj', { rol: `<@&${cfg.DjRoleId}>` })
                    : MessagesService.get(guildId, 'Config:VerSinDj'),
                inline: true
            },
            {
                name: MessagesService.get(guildId, 'Config:VerAi'),
                value: MessagesService.get(guildId, 'Config:VerAiDetalle', {
                    chat: mark(isEnabled(cfg, 'AiChatEnabled', true)),
                    menciones: mark(cfg.AiMentionsEnabled === 1),
                    espontaneo: mark(cfg.AiSpontaneousEnabled === 1)
                }),
                inline: false
            },
            {
                name: MessagesService.get(guildId, 'Config:VerDescargas'),
                value: mark(isEnabled(cfg, 'DownloadsEnabled', true)),
                inline: true
            },
            {
                name: MessagesService.get(guildId, 'Config:VerIdioma'),
                value: LANGUAGE_NAMES[cfg.Language] || 'English',
                inline: true
            }
        )
        .setFooter({ text: MessagesService.get(guildId, 'Config:VerPie') });

    const counting = db.prepare('SELECT ChannelId FROM CountingConfigs WHERE GuildId = ?').get(guildId);
    if (counting?.ChannelId) {
        embed.addFields({
            name: MessagesService.get(guildId, 'Config:VerConteo'),
            value: `<#${counting.ChannelId}>`,
            inline: true
        });
    }

    await interaction.reply({ embeds: [embed], ephemeral: true });
}
