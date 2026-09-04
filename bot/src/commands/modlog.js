import { SlashCommandBuilder, PermissionFlagsBits, EmbedBuilder } from 'discord.js';
import db from '../services/database.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
    .setName('modlog')
    .setDescription('Set the channel where moderation incidents are announced')
    .addChannelOption(option =>
        option.setName('channel')
            .setDescription('Text channel for logs')
            .setRequired(true)
    )
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageGuild);

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const channel = interaction.options.getChannel('channel');

    db.prepare('INSERT OR IGNORE INTO GuildConfigs (GuildId) VALUES (?)').run(guildId);
    db.prepare('UPDATE GuildConfigs SET ModLogChannelId = ? WHERE GuildId = ?').run(channel.id, guildId);

    const embed = new EmbedBuilder()
        .setDescription(MessagesService.get(guildId, 'Config:CanalLogsEstablecido', { canal: channel.toString() }))
        .setColor(0x2ECC71);

    await interaction.reply({ embeds: [embed] });
}
