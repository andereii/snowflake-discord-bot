import { PREFIX, resolveCommandName, createPrefixInteraction } from '../../lib/prefix.js';
import MessagesService from '../../services/messagesService.js';

export default async function prefixHandler(message, client) {
    if (!message.content.startsWith(PREFIX)) return;

    const args = message.content.slice(PREFIX.length).trim().split(/ +/);
    const commandName = resolveCommandName(args.shift().toLowerCase());
    const command = client.commands.get(commandName);
    if (!command) return;

    const interaction = createPrefixInteraction(message, client, command, args);
    try {
        await command.execute(interaction);
    } catch (error) {
        console.error(`[bot] prefix ;${commandName}:`, error);
        await message.reply(MessagesService.get(message.guildId, 'Errores:Interno')).catch(() => {});
    }
}
