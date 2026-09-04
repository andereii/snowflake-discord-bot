import { processCountingMessage } from '../../services/countingService.js';

export default async function countingHandler(message, client) {
    if (message.author.bot || !message.guildId) return;
    await processCountingMessage(message);
}
