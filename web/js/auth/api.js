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
        const response = await fetch('https://snowflake-discord-bot-floral-river-8992.fly.dev/api/bot/shared-guilds', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ guildIds: guildIds })
        });
        const data = await response.json();
        const botGuildIds = data.shared.map(g => g.id);

        return adminGuilds.map(guild => ({
            ...guild,
            hasBot: botGuildIds.includes(guild.id)
        }));
    } catch (error) {
        console.error("Error consultando API:", error);
        return adminGuilds.map(guild => ({ ...guild, hasBot: false }));
    }
}
