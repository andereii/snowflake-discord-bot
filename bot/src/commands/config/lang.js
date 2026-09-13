import { PermissionFlagsBits } from 'discord.js';
import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import { getGuildConfig, updateGuildConfig } from '../../services/guildConfig.js';
import MessagesService from '../../services/messagesService.js';

const LANGUAGE_NAMES = { en: 'English', es: 'Español', pt: 'Português' };

export const data = slash('lang', "Change the bot's language on this server", {
    names: esPt('idioma'),
    descriptions: esPt(
        'Cambia el idioma del bot en este servidor',
        'Muda o idioma do bot neste servidor'
    ),
    permissions: PermissionFlagsBits.ManageGuild
}).addStringOption(o =>
    o.setName('language')
        .setDescription('Bot language (empty = show current)')
        .addChoices(
            { name: 'English', value: 'en' },
            { name: 'Español', value: 'es' },
            { name: 'Português', value: 'pt' }
        )
);

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const selected = interaction.options.getString('language');

    if (!selected) {
        const current = getGuildConfig(guildId).Language || 'en';
        return interaction.reply({
            content: `${MessagesService.get(guildId, 'Config:VerIdioma')}: **${LANGUAGE_NAMES[current] || current}**`,
            ephemeral: true
        });
    }

    updateGuildConfig(guildId, { Language: selected });
    await interaction.reply(MessagesService.get(selected, 'Config:IdiomaCambiado', {
        idioma: LANGUAGE_NAMES[selected] || selected
    }));
}
