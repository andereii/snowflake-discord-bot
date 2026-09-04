import 'dotenv/config';
import { Client, GatewayIntentBits, Partials, Collection } from 'discord.js';
import { registerEvents } from './events/index.js';
import db from './services/database.js';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

// Global crash prevention for asynchronous Discord API errors
process.on('unhandledRejection', (reason, promise) => {
  console.error('[bot] Unhandled Rejection at:', promise, 'reason:', reason);
});

process.on('uncaughtException', (err) => {
  console.error('[bot] Uncaught Exception:', err);
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

// Registrar comandos en el bot
client.commands = new Collection();
const commandsPath = path.join(__dirname, 'commands');
const commandFiles = fs.readdirSync(commandsPath).filter(file => file.endsWith('.js') && file !== 'index.js');

for (const file of commandFiles) {
  const command = await import(path.join(commandsPath, file));
  if (command.data && command.execute) {
    client.commands.set(command.data.name, command);
  }
}

// Registrar eventos.
registerEvents(client);

// Login.
client.login(process.env.DISCORD_TOKEN).then(() => {
  console.log('[bot] Conectado a Discord');
}).catch(err => {
  console.error('[bot] Error al conectar:', err);
  process.exit(1);
});

// Graceful shutdown.
process.on('SIGINT', () => {
  console.log('[bot] Cerrando...');
  db.close();
  client.destroy();
  process.exit(0);
});
