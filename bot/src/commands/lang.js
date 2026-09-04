import { SlashCommandBuilder, PermissionFlagsBits } from 'discord.js';
import db from '../services/database.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
    .setName('lang')
    .setDescription('Change the bot\'s language on this server')
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageGuild)
    .addStringOption(option =>
        option.setName('language')
            .setDescription('Bot language (empty = show current)')
            .addChoices(
                { name: 'English', value: 'en' },
                { name: 'Español', value: 'es' },
                { name: 'Português', value: 'pt' }
            )
    );

const languageNames = {
    'en': 'English',
    'es': 'Español',
    'pt': 'Português'
};

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const selectedLang = interaction.options.getString('language');

    db.prepare('INSERT OR IGNORE INTO GuildConfigs (GuildId) VALUES (?)').run(guildId);

    if (!selectedLang) {
        const row = db.prepare('SELECT Language FROM GuildConfigs WHERE GuildId = ?').get(guildId);
        const current = row?.Language || 'en';
        return interaction.reply({
            content: `${MessagesService.get(guildId, 'Config:VerIdioma')}: **${languageNames[current] || current}**`,
            ephemeral: true
        });
    }

    db.prepare('UPDATE GuildConfigs SET Language = ? WHERE GuildId = ?').run(selectedLang, guildId);

    // Respond in the newly selected language
    await interaction.reply(MessagesService.get(selectedLang, 'Config:IdiomaCambiado', {
        idioma: languageNames[selectedLang] || selectedLang
    }));
}
