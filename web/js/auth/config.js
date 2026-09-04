// Configuración del frontend (funciona en local y en GitHub Pages).
// GitHub Pages sirve archivos estáticos: el login y las llamadas a la API
// usan rutas absolutas apuntando a la API del bot (Fly.io).

// ID de la aplicación de Discord (bot).
export const CLIENT_ID = '1052318909035970641';

// URL base de la API del bot (Fly.io).
export const API_BASE = 'https://snowflake-discord-bot-floral-river-8992.fly.dev/api';

// Clave de localStorage donde se guarda la API key del panel (opcional).
export const API_KEY_STORAGE = 'snowflake_panel_api_key';

// Redirect URI dinámico: apunta a la URL actual de index.html, de modo que
// funciona tanto en local (http://127.0.0.1:5500/web/index.html) como en
// GitHub Pages (https://<usuario>.github.io/<repo>/web/index.html).
// IMPORTANTE: esta URL debe estar registrada como Redirect URI en la
// aplicación de Discord (Developer Portal → OAuth2 → Redirects).
export const REDIRECT_URI = encodeURIComponent(window.location.origin + window.location.pathname);

export const DISCORD_AUTH_URL =
    `https://discord.com/api/oauth2/authorize?client_id=${CLIENT_ID}` +
    `&redirect_uri=${REDIRECT_URI}&response_type=token&scope=identify%20email%20guilds`;
