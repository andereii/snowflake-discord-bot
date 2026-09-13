import { slash } from '../../lib/slash.js';
import MessagesService from '../../services/messagesService.js';

export const data = slash('ping', 'Check the bot latency');

export async function execute(interaction) {
    const sent = await interaction.reply({ content: '...', fetchReply: true });
    const latency = sent.createdTimestamp - interaction.createdTimestamp;
    await interaction.editReply(MessagesService.get(interaction.guildId, 'Ping:Respuesta', { latencia: latency }));
}
