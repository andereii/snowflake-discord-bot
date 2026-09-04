import { API_BASE, API_KEY_STORAGE } from './config.js';

export async function fetchDiscordData(endpoint, token) {
    try {
        const response = await fetch(`https://discord.com/api/v9${endpoint}`, {
            headers: { 'Authorization': `Bearer ${token}` }
        });
        if (!response.ok) throw new Error(`Error: ${endpoint}`);
        return await response.json();
    } catch (error) {
        console.error(error);
        return null;
    }
}

export async function clasificarServidores(adminGuilds) {
    try {
        const guildIds = adminGuilds.map(g => g.id);
        const headers = { 'Content-Type': 'application/json' };
        const key = localStorage.getItem(API_KEY_STORAGE);
        if (key) headers['X-Api-Key'] = key;

        const response = await fetch(`${API_BASE}/bot/shared-guilds`, {
            method: 'POST',
            headers,
            body: JSON.stringify({ guildIds: guildIds })
        });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const data = await response.json();
        const botGuildIds = (data.shared || []).map(g => g.id);

        return adminGuilds.map(guild => ({
            ...guild,
            hasBot: botGuildIds.includes(guild.id)
        }));
    } catch (error) {
        console.error("Error consultando API:", error);
        return adminGuilds.map(guild => ({ ...guild, hasBot: false }));
    }
}
