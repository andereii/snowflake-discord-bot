// Proxy al bot de Discord para obtener datos en tiempo real (stats, miembros, roles, etc.)
// El bot C# expone estos endpoints; el frontend React llama a este Express, que reenvía al bot.
import axios from 'axios';

const BOT_API = process.env.BOT_API_URL || 'http://localhost:8080';

export async function fetchGuildStats(guildId) {
  try {
    const { data } = await axios.get(`${BOT_API}/api/guilds/${guildId}/stats`, { timeout: 5000 });
    return data;
  } catch (err) {
    return null;
  }
}

export async function fetchGuildMembers(guildId) {
  try {
    const { data } = await axios.get(`${BOT_API}/api/guilds/${guildId}/members`, { timeout: 10000 });
    return data?.members ?? [];
  } catch (err) {
    return [];
  }
}

export async function fetchGuildRoles(guildId) {
  try {
    const { data } = await axios.get(`${BOT_API}/api/guilds/${guildId}/roles`, { timeout: 5000 });
    return data?.roles ?? [];
  } catch (err) {
    return [];
  }
}

export async function fetchGuildEmojis(guildId) {
  try {
    const { data } = await axios.get(`${BOT_API}/api/guilds/${guildId}/emojis`, { timeout: 5000 });
    return data?.emojis ?? [];
  } catch (err) {
    return [];
  }
}

export async function fetchGuildChannels(guildId) {
  try {
    const { data } = await axios.get(`${BOT_API}/api/guilds/${guildId}/channels`, { timeout: 5000 });
    return data?.channels ?? [];
  } catch (err) {
    return [];
  }
}
