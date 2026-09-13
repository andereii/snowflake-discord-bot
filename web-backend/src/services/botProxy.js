import axios from 'axios';

const BOT_API = process.env.BOT_API_URL || 'http://localhost:8080';

async function get(path, fallback) {
    try {
        const { data } = await axios.get(`${BOT_API}${path}`, { timeout: 5000 });
        return data;
    } catch {
        return fallback;
    }
}

export const fetchGuildStats = (guildId) => get(`/api/guilds/${guildId}/stats`, null);
export const fetchGuildMembers = async (guildId) =>
    (await get(`/api/guilds/${guildId}/members`, null))?.members ?? [];
export const fetchGuildRoles = async (guildId) =>
    (await get(`/api/guilds/${guildId}/roles`, null))?.roles ?? [];
export const fetchGuildEmojis = async (guildId) =>
    (await get(`/api/guilds/${guildId}/emojis`, null))?.emojis ?? [];
export const fetchGuildChannels = async (guildId) =>
    (await get(`/api/guilds/${guildId}/channels`, null))?.channels ?? [];
