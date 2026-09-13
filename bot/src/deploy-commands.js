import 'dotenv/config';
import path from 'path';
import { fileURLToPath } from 'url';
import { REST, Routes, Client, GatewayIntentBits } from 'discord.js';
import { loadCommands } from './lib/commandLoader.js';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const commands = (await loadCommands(path.join(__dirname, 'commands'))).map(c => c.data.toJSON());

const rest = new REST().setToken(process.env.DISCORD_TOKEN);
const clientId = process.env.DISCORD_CLIENT_ID;

await rest.put(Routes.applicationCommands(clientId), { body: [] });
console.log('[deploy] Cleared global commands');

const client = new Client({ intents: [GatewayIntentBits.Guilds] });
await client.login(process.env.DISCORD_TOKEN);

for (const guildId of client.guilds.cache.map(g => g.id)) {
    try {
        await rest.put(Routes.applicationGuildCommands(clientId, guildId), { body: commands });
        console.log(`[deploy] ${commands.length} commands registered in ${guildId}`);
    } catch (err) {
        console.error(`[deploy] Failed for ${guildId}`, err);
    }
}

client.destroy();
console.log('[deploy] Done');
