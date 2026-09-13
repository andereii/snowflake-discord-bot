import { processCountingMessage } from '../../services/countingService.js';

export default async function countingHandler(message) {
    if (message.author.bot || !message.guildId) return;
    await processCountingMessage(message);
}
