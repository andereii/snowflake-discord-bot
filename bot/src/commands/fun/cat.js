import { EmbedBuilder } from 'discord.js';
import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import MessagesService from '../../services/messagesService.js';

export const data = slash('cat', 'Show a random cat picture', {
    names: esPt('gato'),
    descriptions: esPt('Muestra una foto aleatoria de un gato', 'Mostra uma foto aleatória de um gato')
});

function mewTitle() {
    let title = (Math.random() < 0.5 ? 'M' : 'm')
        + 'e'.repeat(Math.floor(Math.random() * 20) + 1)
        + 'w'.repeat(Math.floor(Math.random() * 10) + 1);
    const bangs = Math.floor(Math.random() * 11);
    const questions = Math.floor(Math.random() * 11);
    if (bangs) title += '!'.repeat(bangs);
    if (questions) title += '?'.repeat(questions);
    if (Math.random() < 0.5) title += ' :' + '3'.repeat(Math.floor(Math.random() * 5) + 1);
    return title;
}

export async function execute(interaction) {
    const guildId = interaction.guildId;
    await interaction.deferReply();

    try {
        const response = await fetch('https://api.thecatapi.com/v1/images/search');
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const data = await response.json();
        const url = data?.[0]?.url;
        if (!url) {
            return interaction.editReply(MessagesService.get(guildId, 'Gato:Error'));
        }
        await interaction.editReply({
            embeds: [
                new EmbedBuilder()
                    .setTitle(mewTitle())
                    .setImage(url)
                    .setFooter({ text: url })
                    .setColor('#f9c2d1')
            ]
        });
    } catch (error) {
        console.error('[cat]', error);
        await interaction.editReply(MessagesService.get(guildId, 'Gato:Error'));
    }
}
