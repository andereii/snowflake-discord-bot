import { API_BASE, API_KEY_STORAGE } from '../auth/config.js';

// Clave de API del panel guardada en el navegador (opcional).
export function getSavedApiKey() {
    return localStorage.getItem(API_KEY_STORAGE) || '';
}

export function saveApiKey(key) {
    if (key) localStorage.setItem(API_KEY_STORAGE, key);
    else localStorage.removeItem(API_KEY_STORAGE);
}

function authHeaders() {
    const headers = { 'Content-Type': 'application/json' };
    const key = getSavedApiKey();
    if (key) headers['X-Api-Key'] = key;
    return headers;
}

async function request(url, options) {
    const response = await fetch(url, options);
    if (!response.ok) {
        const err = new Error(`HTTP ${response.status}`);
        err.status = response.status;
        throw err;
    }
    if (response.status === 204) return null;
    return await response.json();
}

/** Snapshot completo de la configuración del servidor. */
export async function getGuildConfig(guildId) {
    try {
        return await request(`${API_BASE}/guilds/${guildId}/config`, { headers: { 'Content-Type': 'application/json' } });
    } catch (error) {
        console.error(error);
        return null;
    }
}

/** Actualiza la configuración general (patch: solo los campos enviados). */
export async function updateGuildConfig(guildId, payload) {
    return await request(`${API_BASE}/guilds/${guildId}/config`, {
        method: 'POST',
        headers: authHeaders(),
        body: JSON.stringify(payload)
    });
}

/** Actualiza el juego de conteo. */
export async function updateCounting(guildId, payload) {
    return await request(`${API_BASE}/guilds/${guildId}/config/counting`, {
        method: 'POST',
        headers: authHeaders(),
        body: JSON.stringify(payload)
    });
}

/** Crea o actualiza la suscripción de YouTube. */
export async function updateYouTube(guildId, payload) {
    return await request(`${API_BASE}/guilds/${guildId}/config/youtube`, {
        method: 'POST',
        headers: authHeaders(),
        body: JSON.stringify(payload)
    });
}

/** Elimina la suscripción de YouTube del servidor. */
export async function deleteYouTube(guildId) {
    return await request(`${API_BASE}/guilds/${guildId}/config/youtube`, {
        method: 'DELETE',
        headers: authHeaders()
    });
}

/** Estadísticas del servidor para el widget de Inicio. */
export async function getGuildStats(guildId) {
    try {
        return await request(`${API_BASE}/guilds/${guildId}/stats`, { headers: { 'Content-Type': 'application/json' } });
    } catch (error) {
        console.error(error);
        return null;
    }
}

/** Lista de miembros del servidor (humano, sin bots). */
export async function getGuildMembers(guildId) {
    try {
        const data = await request(`${API_BASE}/guilds/${guildId}/members`, { headers: { 'Content-Type': 'application/json' } });
        return data?.members ?? [];
    } catch (error) {
        console.error(error);
        return [];
    }
}

/** Lista de roles del servidor (sin @everyone). */
export async function getGuildRoles(guildId) {
    try {
        const data = await request(`${API_BASE}/guilds/${guildId}/roles`, { headers: { 'Content-Type': 'application/json' } });
        return data?.roles ?? [];
    } catch (error) {
        console.error(error);
        return [];
    }
}

/** Configuración de cumpleaños del servidor. */
export async function getBirthdayConfig(guildId) {
    try {
        return await request(`${API_BASE}/guilds/${guildId}/config/birthday`, { headers: { 'Content-Type': 'application/json' } });
    } catch (error) {
        console.error(error);
        return null;
    }
}

/** Actualiza la configuración de cumpleaños. */
export async function updateBirthdayConfig(guildId, payload) {
    return await request(`${API_BASE}/guilds/${guildId}/config/birthday`, {
        method: 'POST',
        headers: authHeaders(),
        body: JSON.stringify(payload)
    });
}
