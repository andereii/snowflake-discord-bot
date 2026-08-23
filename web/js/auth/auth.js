import { DISCORD_AUTH_URL } from './config.js';
import { fetchDiscordData, clasificarServidores } from './api.js';
import { renderUserPill, renderServerCards, setupModals } from './ui.js';

function setupLoginButton() {
    const loginBtn = document.getElementById('discord-login-btn');
    if (loginBtn) {
        loginBtn.href = DISCORD_AUTH_URL;
    }
}

function checkUrlForToken() {
    const fragment = new URLSearchParams(window.location.hash.slice(1));
    const accessToken = fragment.get('access_token');

    if (accessToken) {
        localStorage.setItem('discord_token', accessToken);
        window.history.replaceState(null, '', window.location.pathname);
    }
}

async function init() {
    setupModals();
    checkUrlForToken();
    const token = localStorage.getItem('discord_token');

    if (token) {
        const [user, guilds] = await Promise.all([
            fetchDiscordData('/users/@me', token),
            fetchDiscordData('/users/@me/guilds', token)
        ]);

        if (user && guilds) {
            renderUserPill(user);
            const adminGuilds = guilds.filter(guild => (BigInt(guild.permissions) & 8n) === 8n);
            const classifiedGuilds = await clasificarServidores(adminGuilds);
            renderServerCards(classifiedGuilds);
        } else {
            localStorage.removeItem('discord_token');
            setupLoginButton();
        }
    } else {
        setupLoginButton();
    }
}

window.addEventListener('DOMContentLoaded', init);
