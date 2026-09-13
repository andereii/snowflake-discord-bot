import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import { searchImages, buildEmbed, buildButtons, registerSession } from '../../services/imageSearchWidget.js';
import MessagesService from '../../services/messagesService.js';

export const data = slash('image', 'Search for an image on the web', {
    names: esPt('imagen', 'imagem'),
    descriptions: esPt('Busca una imagen en la web', 'Busca uma imagem na web')
}).addStringOption(o => o.setName('query').setDescription('What image to search for').setRequired(true));

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const query = interaction.options.getString('query');
    await interaction.deferReply();

    const urls = await searchImages(query);
    if (!urls?.length) {
        return interaction.editReply({
            content: `❌ ${MessagesService.get(guildId, 'Herramientas:BusquedaSinResultados', { query })}`
        });
    }

    const message = await interaction.editReply({
        embeds: [buildEmbed(query, urls, 0)],
        components: [buildButtons()]
    });
    registerSession(message.id, interaction.user.id, query, urls);
}
