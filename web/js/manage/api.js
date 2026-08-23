const API_BASE = 'https://snowflake-discord-bot-floral-river-8992.fly.dev/api';

export async function getGuildConfig(guildId) {
    try {
        const response = await fetch(`${API_BASE}/guilds/${guildId}/config`);
        if (!response.ok) throw new Error('No se pudo obtener la configuración');
        return await response.json();
    } catch (error) {
        console.error(error);
        return null;
    }
}

export async function updateGuildConfig(guildId, payload) {
    try {
        const response = await fetch(`${API_BASE}/guilds/${guildId}/config`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(payload)
        });
        if (!response.ok) throw new Error('Error al actualizar la configuración');
        return await response.json();
    } catch (error) {
        console.error(error);
        throw error;
    }
}
