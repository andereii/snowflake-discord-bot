import axios from 'axios';

export const CLIENT_ID = '1052318909035970641';
export const API_KEY_STORAGE = 'snowflake_panel_api_key';

export const api = axios.create({ withCredentials: true });

api.interceptors.request.use((config) => {
    const key = localStorage.getItem(API_KEY_STORAGE);
    if (key) config.headers['X-Api-Key'] = key;
    return config;
});

export const fallbackAvatar = 'https://cdn.discordapp.com/embed/avatars/0.png';

export function guildIconUrl(guild, size = 128) {
    if (!guild?.id || !guild?.icon) return fallbackAvatar;
    return `https://cdn.discordapp.com/icons/${guild.id}/${guild.icon}.png?size=${size}`;
}

export function guildBannerUrl(guild) {
    if (!guild?.id || !guild?.banner) return null;
    return `https://cdn.discordapp.com/banners/${guild.id}/${guild.banner}.png?size=480`;
}

export function userAvatarUrl(user) {
    if (!user?.id || !user?.avatar) return fallbackAvatar;
    return `https://cdn.discordapp.com/avatars/${user.id}/${user.avatar}.png`;
}

export function botInviteUrl(guildId) {
    const params = new URLSearchParams({
        client_id: CLIENT_ID,
        permissions: '8',
        scope: 'bot applications.commands',
        guild_id: guildId,
        disable_guild_select: 'true'
    });
    return `https://discord.com/oauth2/authorize?${params.toString()}`;
}

export async function fetchMe() {
    const { data } = await api.get('/api/auth/me');
    return data.user;
}

export async function fetchGuilds() {
    const { data } = await api.get('/api/auth/guilds');
    return data.guilds || [];
}

export async function logout() {
    await api.post('/api/auth/logout');
}
