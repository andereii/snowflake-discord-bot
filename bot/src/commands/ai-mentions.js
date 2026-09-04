import { SlashCommandBuilder, PermissionFlagsBits } from 'discord.js';
import db from '../services/database.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
    .setName('ai-mentions')
    .setDescription('Enable or disable AI responses when the bot is mentioned with @')
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageGuild)
    .addStringOption(option =>
        option.setName('state')
            .setDescription('Enable or disable (empty = show current state)')
            .addChoices(
                { name: 'Enable', value: 'on' },
                { name: 'Disable', value: 'off' }
            )
    );

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const state = interaction.options.getString('state');

    db.prepare('INSERT OR IGNORE INTO GuildConfigs (GuildId) VALUES (?)').run(guildId);

    if (state === 'on' || state === 'off') {
        const value = state === 'on' ? 1 : 0;
        db.prepare('UPDATE GuildConfigs SET AiMentionsEnabled = ? WHERE GuildId = ?').run(value, guildId);
        await interaction.reply(
            state === 'on'
                ? MessagesService.get(guildId, 'Chat:MencionesActivadas')
                : MessagesService.get(guildId, 'Chat:MencionesDesactivadas')
        );
    } else {
        const row = db.prepare('SELECT AiMentionsEnabled FROM GuildConfigs WHERE GuildId = ?').get(guildId);
        const enabled = row?.AiMentionsEnabled === 1;
        await interaction.reply({
            content: enabled
                ? MessagesService.get(guildId, 'Chat:MencionesActivadas')
                : MessagesService.get(guildId, 'Chat:MencionesDesactivadas'),
            ephemeral: true
        });
    }
}
