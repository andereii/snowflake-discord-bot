import 'dotenv/config';
import path from 'path';
import { fileURLToPath } from 'url';
import { Client, GatewayIntentBits, Partials, Collection } from 'discord.js';
import { registerEvents } from './events/index.js';
import { loadCommands } from './lib/commandLoader.js';
import { startInternalApi } from './lib/internalApi.js';
import { runtimeName } from './lib/runtime.js';
import db from './services/database.js';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

process.on('unhandledRejection', (reason) => {
    console.error('[bot] unhandledRejection:', reason);
});
process.on('uncaughtException', (err) => {
    console.error('[bot] uncaughtException:', err);
});

const client = new Client({
    intents: [
        GatewayIntentBits.Guilds,
        GatewayIntentBits.GuildMembers,
        GatewayIntentBits.GuildMessages,
        GatewayIntentBits.MessageContent,
        GatewayIntentBits.GuildVoiceStates,
        GatewayIntentBits.GuildMessageReactions
    ],
    partials: [Partials.Message, Partials.Channel, Partials.Reaction]
});

client.commands = new Collection();
const commands = await loadCommands(path.join(__dirname, 'commands'));
for (const command of commands) {
    client.commands.set(command.data.name, command);
}

registerEvents(client);

const internalPort = Number(process.env.BOT_API_PORT) || 8080;
const internalApi = startInternalApi(client, {
    port: internalPort,
    secret: process.env.BOT_INTERNAL_SECRET || ''
});

client.login(process.env.DISCORD_TOKEN).then(() => {
    console.log(`[bot] runtime: ${runtimeName()}`);
    console.log(`[bot] Connected (${commands.length} commands loaded)`);
}).catch(err => {
    console.error('[bot] Login failed:', err);
    process.exit(1);
});

process.on('SIGINT', () => {
    console.log('[bot] Shutting down');
    internalApi.close();
    db.close();
    client.destroy();
    process.exit(0);
});
