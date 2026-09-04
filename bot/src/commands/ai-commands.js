import { SlashCommandBuilder, PermissionFlagsBits } from 'discord.js';
import db from '../services/database.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
    .setName('ai-commands')
    .setDescription('Enable or disable executing bot commands from AI chat')
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
        db.prepare('UPDATE GuildConfigs SET AiCommandsEnabled = ? WHERE GuildId = ?').run(value, guildId);
        await interaction.reply(
            state === 'on'
                ? MessagesService.get(guildId, 'Chat:ComandosActivados')
                : MessagesService.get(guildId, 'Chat:ComandosDesactivados')
        );
    } else {
        const row = db.prepare('SELECT AiCommandsEnabled FROM GuildConfigs WHERE GuildId = ?').get(guildId);
        const enabled = !row || row.AiCommandsEnabled !== 0;
        await interaction.reply({
            content: enabled
                ? MessagesService.get(guildId, 'Chat:ComandosActivados')
                : MessagesService.get(guildId, 'Chat:ComandosDesactivados'),
            ephemeral: true
        });
    }
}
