import { SlashCommandBuilder, EmbedBuilder } from 'discord.js';
import db from '../services/database.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
    .setName('show')
    .setDescription('Show the summary of all bot settings on this server');

function siNo(val) {
    return val ? '✅' : '❌';
}

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const cfg = db.prepare('SELECT * FROM GuildConfigs WHERE GuildId = ?').get(guildId) || {};

    const languageNames = {
        'es': 'Español',
        'pt': 'Português',
        'en': 'English'
    };

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
                value: cfg.DjRoleId ? MessagesService.get(guildId, 'Config:VerDj', { rol: `<@&${cfg.DjRoleId}>` }) : MessagesService.get(guildId, 'Config:VerSinDj'),
                inline: true
            },
            {
                name: MessagesService.get(guildId, 'Config:VerAi'),
                value: MessagesService.get(guildId, 'Config:VerAiDetalle', {
                    chat: siNo(cfg.AiChatEnabled !== 0),
                    menciones: siNo(cfg.AiMentionsEnabled === 1),
                    espontaneo: siNo(cfg.AiSpontaneousEnabled === 1)
                }),
                inline: false
            },
            {
                name: MessagesService.get(guildId, 'Config:VerDescargas'),
                value: siNo(cfg.DownloadsEnabled !== 0),
                inline: true
            },
            {
                name: MessagesService.get(guildId, 'Config:VerIdioma'),
                value: languageNames[cfg.Language] || 'English',
                inline: true
            }
        )
        .setFooter({ text: MessagesService.get(guildId, 'Config:VerPie') });

    try {
        const counting = db.prepare('SELECT * FROM CountingConfigs WHERE GuildId = ?').get(guildId);
        if (counting?.ChannelId) {
            embed.addFields({
                name: MessagesService.get(guildId, 'Config:VerConteo'),
                value: `<#${counting.ChannelId}>`,
                inline: true
            });
        }
    } catch {}

    await interaction.reply({ embeds: [embed], ephemeral: true });
}
