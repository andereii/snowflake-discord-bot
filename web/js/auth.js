/**
 * @constant {string}
 * @description Client ID del bot Snowflake.
 */
const CLIENT_ID = '1052318909035970641';

/**
 * @constant {string}
 * @description URL a la que Discord redirigirá tras el login.
 */
const REDIRECT_URI = encodeURIComponent('http://127.0.0.1:5500/web/index.html');

/**
 * @constant {string}
 * @description URL de autorización de OAuth2 con scopes "identify", "email" y "guilds".
 */
const DISCORD_AUTH_URL = `https://discord.com/api/oauth2/authorize?client_id=${CLIENT_ID}&redirect_uri=${REDIRECT_URI}&response_type=token&scope=identify%20email%20guilds`;

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

async function fetchDiscordData(endpoint, token) {
    try {
        const response = await fetch(`https://discord.com/api/v9${endpoint}`, {
            headers: { 'Authorization': `Bearer ${token}` }
        });
        if (!response.ok) throw new Error(`Error en la petición a Discord: ${endpoint}`);
        return await response.json();
    } catch (error) {
        console.error(`Error obteniendo ${endpoint}:`, error);
        return null;
    }
}

function renderUserPill(user) {
    const authContainer = document.getElementById('auth-container');
    const template = document.getElementById('user-pill-template');

    if (authContainer && template) {
        authContainer.replaceChildren();
        const clone = template.content.cloneNode(true);

        const tagText = clone.getElementById('user-tag-text');
        tagText.textContent = `@${user.username}`;

        const avatarImg = clone.getElementById('user-avatar-img');
        if (user.avatar) {
            avatarImg.src = `https://cdn.discordapp.com/avatars/${user.id}/${user.avatar}.png`;
        } else {
            avatarImg.src = `https://cdn.discordapp.com/embed/avatars/${parseInt(user.discriminator || '0') % 5}.png`;
        }

        authContainer.appendChild(clone);
        setupDropdownEvents();
    }
}

function setupDropdownEvents() {
    const pillBtn = document.getElementById('user-pill-btn');
    const dropdownMenu = document.getElementById('user-dropdown-menu');
    const logoutBtn = document.getElementById('logout-btn');

    pillBtn.addEventListener('click', (event) => {
        event.stopPropagation();
        dropdownMenu.classList.toggle('show');
        pillBtn.setAttribute('aria-expanded', dropdownMenu.classList.contains('show').toString());
    });

    logoutBtn.addEventListener('click', () => {
        localStorage.removeItem('discord_token');
        window.location.reload(); 
    });

    document.addEventListener('click', (event) => {
        if (!pillBtn.contains(event.target) && !dropdownMenu.contains(event.target)) {
            dropdownMenu.classList.remove('show');
            pillBtn.setAttribute('aria-expanded', 'false');
        }
    });
}

/**
 * @async
 * @function filtrarServidoresDelBot
 * @description Cruza la lista de servidores del usuario con la futura API en C# de Snowflake.
 * @param {Array} adminGuilds - Lista de servidores donde el usuario es administrador.
 * @returns {Promise<Array>}
 */
async function filtrarServidoresDelBot(adminGuilds) {
    try {
        // ===================================================================
        // FUTURA CONEXIÓN CON TU BACKEND EN C#
        // Descomenta este bloque cuando tu bot tenga una API HTTP activa.
        // ===================================================================
        
        /*
        const guildIds = adminGuilds.map(g => g.id);

        const response = await fetch('http://127.0.0.1:5000/api/check-guilds', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ ids: guildIds })
        });
        
        const botGuildIds = await response.json(); 
        return adminGuilds.filter(guild => botGuildIds.includes(guild.id));
        */

        // ===================================================================
        // POR AHORA (Simulación)
        // Muestra los servidores que son de admin para que pruebes el UI.
        // ===================================================================
        return adminGuilds; 

    } catch (error) {
        console.error("Error al consultar la API de Snowflake:", error);
        return [];
    }
}

function renderServerCards(validGuilds) {
    const grid = document.getElementById('servers-grid');
    const template = document.getElementById('server-card-template');
    
    document.getElementById('login-prompt').classList.remove('show');
    document.getElementById('servers-dashboard').classList.add('show');

    if (!grid || !template) return;
    grid.replaceChildren();

    if (validGuilds.length === 0) {
        grid.innerHTML = '<p style="color: var(--color-text-muted); grid-column: 1 / -1; text-align: center;">No se encontraron servidores configurables.</p>';
        return;
    }

    validGuilds.forEach(guild => {
        const clone = template.content.cloneNode(true);
        
        clone.querySelector('.server-name').textContent = guild.name;
        
        const iconImg = clone.querySelector('.server-icon');
        if (guild.icon) {
            iconImg.src = `https://cdn.discordapp.com/icons/${guild.id}/${guild.icon}.png`;
        } else {
            iconImg.src = 'https://cdn.discordapp.com/embed/avatars/0.png'; 
        }

        const manageBtn = clone.querySelector('.btn-manage');
        manageBtn.href = `manage.html?server_id=${guild.id}`;

        grid.appendChild(clone);
    });
}

async function init() {
    checkUrlForToken();
    const token = localStorage.getItem('discord_token');

    if (token) {
        const [user, guilds] = await Promise.all([
            fetchDiscordData('/users/@me', token),
            fetchDiscordData('/users/@me/guilds', token)
        ]);

        if (user && guilds) {
            renderUserPill(user);

            // Filtramos donde el usuario es Admin (0x8)
            const adminGuilds = guilds.filter(guild => (BigInt(guild.permissions) & 8n) === 8n);
            
            // Filtramos donde el bot está presente a través de la API
            const botGuilds = await filtrarServidoresDelBot(adminGuilds);

            renderServerCards(botGuilds);
        } else {
            localStorage.removeItem('discord_token');
            setupLoginButton();
        }
    } else {
        setupLoginButton();
    }
}

window.addEventListener('DOMContentLoaded', init);