export const CLIENT_ID = '1052318909035970641';
export const REDIRECT_URI = encodeURIComponent('http://127.0.0.1:5500/web/index.html');
export const DISCORD_AUTH_URL = `https://discord.com/api/oauth2/authorize?client_id=${CLIENT_ID}&redirect_uri=${REDIRECT_URI}&response_type=token&scope=identify%20email%20guilds`;
