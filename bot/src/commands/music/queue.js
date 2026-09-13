import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import { buildQueueEmbed } from '../../services/music.js';
import MessagesService from '../../services/messagesService.js';

export const data = slash('queue', 'Show the current song and the queue', {
    names: esPt('cola', 'fila'),
    descriptions: esPt('Muestra la canción actual y la cola', 'Mostra a música atual e a fila')
});

export async function execute(interaction) {
    const embed = buildQueueEmbed(interaction.guildId);
    if (!embed) {
        return interaction.reply({
            content: MessagesService.get(interaction.guildId, 'Musica:ColaVacia'),
            ephemeral: true
        });
    }
    await interaction.reply({ embeds: [embed] });
}
