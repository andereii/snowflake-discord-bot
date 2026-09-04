import 'dotenv/config';
import { Client, GatewayIntentBits } from 'discord.js';
const client = new Client({ intents: [GatewayIntentBits.Guilds] });
client.login(process.env.DISCORD_TOKEN).then(() => {
    client.guilds.cache.forEach(g => console.log(g.name, g.id));
    process.exit(0);
});
