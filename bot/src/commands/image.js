import { SlashCommandBuilder } from 'discord.js';
import { searchImages, buildEmbed, buildButtons, registerSession } from '../services/imageSearchWidget.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
    .setName('image')
    .setDescription('Search for an image on the web')
    .addStringOption(option =>
        option.setName('query')
            .setDescription('What image to search for')
            .setRequired(true)
    );

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const query = interaction.options.getString('query');

    await interaction.deferReply();

    const urls = await searchImages(query);
    if (!urls || urls.length === 0) {
        return interaction.editReply({
            content: `❌ ${MessagesService.get(guildId, 'Herramientas:BusquedaSinResultados', { query })}`
        });
    }

    const embed = buildEmbed(query, urls, 0);
    const buttons = buildButtons();

    const message = await interaction.editReply({
        embeds: [embed],
        components: [buttons]
    });

    registerSession(message.id, interaction.user.id, query, urls);
}
