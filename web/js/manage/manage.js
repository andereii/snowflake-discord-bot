import {
    getGuildConfig,
    updateGuildConfig,
    updateCounting,
    updateYouTube,
    deleteYouTube,
    getSavedApiKey,
    saveApiKey,
    getGuildStats,
    getGuildMembers,
    getGuildRoles,
    getBirthdayConfig,
    updateBirthdayConfig
} from './api.js';
import { setupSidebar, showToast, withLoading, readOptional, readOptionalNumber } from './ui.js';

// Extraer el server_id de la URL (ej: manage.html?server_id=123456)
const urlParams = new URLSearchParams(window.location.search);
const guildId = urlParams.get('server_id');

async function init() {
    setupSidebar();

    if (!guildId) {
        showToast('Error: No se especificó el ID del servidor en la URL', 'error');
        document.querySelector('.manage-container').innerHTML = '<h2>Servidor no encontrado.</h2>';
        return;
    }

    // 1. Cargar la clave de API guardada (si existe).
    document.getElementById('apiKeyInput').value = getSavedApiKey();

    // 2. Obtener configuraciones actuales.
    const config = await getGuildConfig(guildId);
    if (config) {
        populateForms(config);
    } else {
        showToast('Error al cargar la configuración', 'error');
    }

    // 2b. Cumpleaños: la config no viene en el snapshot principal.
    const birthdayConfig = await getBirthdayConfig(guildId);
    if (birthdayConfig) {
        setToggle('birthdayEnabled', birthdayConfig.enabled);
        setInput('birthdayChannelId', birthdayConfig.channelId);
        setInput('birthdayHourUtc', birthdayConfig.hourUtc);
        setInput('birthdayMessage', birthdayConfig.message);
    }

    // 3. Cargar el widget de Inicio (stats, usuarios y roles).
    loadHome();

    // 4. Conectar los botones de guardado de cada sección.
    bindSaveButtons();

    // 5. Botones de acceso rápido del widget de Inicio.
    document.querySelectorAll('[data-goto]').forEach(btn => {
        btn.addEventListener('click', () => {
            const target = btn.getAttribute('data-goto');
            const link = document.querySelector(`.sidebar-menu-item[href="#${target}"]`);
            if (link) link.click();
        });
    });
}

// ---------------------------------------------------------------------------
// Widget de Inicio
// ---------------------------------------------------------------------------

async function loadHome() {
    const [stats, members, roles] = await Promise.all([
        getGuildStats(guildId),
        getGuildMembers(guildId),
        getGuildRoles(guildId)
    ]);

    if (stats) {
        document.getElementById('home-server-name').textContent = stats.name || 'Servidor';
        const icon = document.getElementById('home-server-icon');
        icon.src = stats.iconUrl || 'https://cdn.discordapp.com/embed/avatars/0.png';
        document.getElementById('home-members').textContent = stats.memberCount ?? '–';
        document.getElementById('home-channels').textContent = stats.channelCount ?? '–';
        document.getElementById('home-roles').textContent = stats.roleCount ?? '–';
    }

    renderUsers(members);
    renderRoles(roles);
}

function renderUsers(members) {
    const ul = document.getElementById('home-users-list');
    if (!ul) return;
    ul.replaceChildren();
    if (!members.length) {
        ul.innerHTML = '<li class="home-list-empty">No hay usuarios para mostrar.</li>';
        return;
    }
    members.forEach(m => {
        const li = document.createElement('li');
        const img = document.createElement('img');
        img.className = 'home-avatar';
        img.src = m.avatarUrl || 'https://cdn.discordapp.com/embed/avatars/0.png';
        img.alt = m.displayName;
        const span = document.createElement('span');
        span.textContent = m.displayName || m.username;
        li.appendChild(img);
        li.appendChild(span);
        ul.appendChild(li);
    });
}

function renderRoles(roles) {
    const ul = document.getElementById('home-roles-list');
    if (!ul) return;
    ul.replaceChildren();
    if (!roles.length) {
        ul.innerHTML = '<li class="home-list-empty">No hay roles para mostrar.</li>';
        return;
    }
    roles.forEach(r => {
        const li = document.createElement('li');
        const dot = document.createElement('span');
        dot.className = 'home-role-dot';
        dot.style.backgroundColor = '#' + (r.color || '95A5A6');
        const span = document.createElement('span');
        span.textContent = r.name;
        li.appendChild(dot);
        li.appendChild(span);
        ul.appendChild(li);
    });
}

// ---------------------------------------------------------------------------
// Poblar formularios con el snapshot
// ---------------------------------------------------------------------------

function populateForms(config) {
    // General
    setSelect('language', config.language || 'en');

    // Moderación
    setInput('modLogChannelId', config.moderation?.logChannelId);

    // Bienvenida
    setInput('welcomeChannelId', config.welcome?.channelId);
    setInput('welcomeMessage', config.welcome?.message);

    // Música
    setInput('volume', config.music?.volume);
    setInput('djRoleId', config.music?.djRoleId);

    // IA
    setToggle('aiChatEnabled', config.ai?.chatEnabled);
    setToggle('aiMentionsEnabled', config.ai?.mentionsEnabled);
    setToggle('aiSpontaneousEnabled', config.ai?.spontaneousEnabled);
    setToggle('aiWebSearchEnabled', config.ai?.webSearchEnabled);
    setToggle('aiCommandsEnabled', config.ai?.commandsEnabled);

    // Conteo
    setInput('countingChannelId', config.counting?.channelId);
    setSelect('countingBase', config.counting?.base || 'Decimal');
    setInput('countingGoal', config.counting?.goal);
    setInput('countingChances', config.counting?.extraChancesPerDay);
    setInput('countingEmojiCorrect', config.counting?.emojiCorrect);
    setInput('countingEmojiIncorrect', config.counting?.emojiIncorrect);
    setInput('countingEmojiRecord', config.counting?.emojiRecord);
    setInput('countingLoseMessage', config.counting?.loseMessage);

    // YouTube
    setInput('ytChannelId', config.youtube?.channelId);
    setInput('ytChannelName', config.youtube?.channelName);
    setInput('ytNotifyChannelId', config.youtube?.notifyChannelId);
    setInput('ytNotifyRoleId', config.youtube?.notifyRoleId);
    setInput('ytCustomMessage', config.youtube?.customMessage);

    // Voces
    setInput('hubChannelId', config.voice?.hubChannelId);
    setInput('tempChannelNameTemplate', config.voice?.tempChannelNameTemplate);

    // Descargas
    setToggle('downloadsEnabled', config.downloads?.enabled);

    // Bloqueos (solo lectura)
    renderBlockedChannels(config.blockedChannels || []);
}

// ---------------------------------------------------------------------------
// Helpers de poblado
// ---------------------------------------------------------------------------

function setInput(id, value) {
    const el = document.getElementById(id);
    if (el && value !== null && value !== undefined) el.value = value;
}

function setSelect(id, value) {
    const el = document.getElementById(id);
    if (el && value) el.value = value;
}

function setToggle(id, value) {
    const el = document.getElementById(id);
    if (el && value !== null && value !== undefined) el.checked = Boolean(value);
}

function renderBlockedChannels(list) {
    const ul = document.getElementById('blockedChannelsList');
    if (!ul) return;
    ul.replaceChildren();
    if (!Array.isArray(list) || list.length === 0) {
        ul.innerHTML = '<li style="color: var(--color-text-muted);">No hay canales en bloqueo.</li>';
        return;
    }
    list.forEach(id => {
        const li = document.createElement('li');
        li.className = 'blocked-item';
        li.textContent = `🔒 Canal ${id}`;
        ul.appendChild(li);
    });
}

// ---------------------------------------------------------------------------
// Guardado por sección
// ---------------------------------------------------------------------------

function bindSaveButtons() {
    bindButton('save-general-btn', 'Guardando...', async () => {
        // Guardar la API key en el navegador.
        saveApiKey(document.getElementById('apiKeyInput').value.trim());

        const payload = {};
        const lang = document.getElementById('language').value;
        if (lang) payload.language = lang;
        await updateGuildConfig(guildId, payload);
        return 'Configuración general guardada con éxito';
    });

    bindButton('save-moderation-btn', 'Guardando...', async () => {
        await updateGuildConfig(guildId, { modLogChannelId: readOptional('modLogChannelId') ?? '' });
        return 'Ajustes de moderación guardados con éxito';
    });

    bindButton('save-welcome-btn', 'Guardando...', async () => {
        const payload = { welcomeChannelId: readOptional('welcomeChannelId') ?? '' };
        const msg = readOptional('welcomeMessage');
        if (msg !== null) payload.welcomeMessage = msg;
        await updateGuildConfig(guildId, payload);
        return 'Ajustes de bienvenida guardados con éxito';
    });

    bindButton('save-music-btn', 'Guardando...', async () => {
        const payload = { djRoleId: readOptional('djRoleId') ?? '' };
        const volume = readOptionalNumber('volume');
        if (volume !== null) payload.volume = Math.max(0, Math.min(100, volume));
        await updateGuildConfig(guildId, payload);
        return 'Ajustes de música guardados con éxito';
    });

    bindButton('save-ia-btn', 'Guardando...', async () => {
        const payload = {
            aiChatEnabled: checked('aiChatEnabled'),
            aiMentionsEnabled: checked('aiMentionsEnabled'),
            aiSpontaneousEnabled: checked('aiSpontaneousEnabled'),
            aiWebSearchEnabled: checked('aiWebSearchEnabled'),
            aiCommandsEnabled: checked('aiCommandsEnabled')
        };
        await updateGuildConfig(guildId, payload);
        return 'Ajustes de IA guardados con éxito';
    });

    bindButton('save-counting-btn', 'Guardando...', async () => {
        const payload = { channelId: readOptional('countingChannelId') ?? '' };
        const base = document.getElementById('countingBase')?.value;
        if (base) payload.base = base;
        const goal = readOptionalNumber('countingGoal');
        if (goal !== null) payload.goal = goal;
        const chances = readOptionalNumber('countingChances');
        if (chances !== null) payload.extraChancesPerDay = Math.max(0, Math.min(10, chances));
        setIfPresent(payload, 'emojiCorrect', readOptional('countingEmojiCorrect'));
        setIfPresent(payload, 'emojiIncorrect', readOptional('countingEmojiIncorrect'));
        setIfPresent(payload, 'emojiRecord', readOptional('countingEmojiRecord'));
        setIfPresent(payload, 'loseMessage', readOptional('countingLoseMessage'));
        await updateCounting(guildId, payload);
        return 'Ajustes de conteo guardados con éxito';
    });

    bindButton('save-youtube-btn', 'Guardando...', async () => {
        const payload = { ytChannelId: readOptional('ytChannelId') ?? '' };
        setIfPresent(payload, 'ytChannelName', readOptional('ytChannelName'));
        setIfPresent(payload, 'notifyChannelId', readOptional('ytNotifyChannelId'));
        setIfPresent(payload, 'notifyRoleId', readOptional('ytNotifyRoleId'));
        setIfPresent(payload, 'customMessage', readOptional('ytCustomMessage'));
        await updateYouTube(guildId, payload);
        return 'Suscripción de YouTube guardada con éxito';
    });

    bindButton('delete-youtube-btn', 'Quitando...', async () => {
        await deleteYouTube(guildId);
        return 'Suscripción de YouTube eliminada';
    }, true);

    bindButton('save-voice-btn', 'Guardando...', async () => {
        const payload = { hubChannelId: readOptional('hubChannelId') ?? '' };
        const tpl = readOptional('tempChannelNameTemplate');
        if (tpl !== null) payload.tempChannelNameTemplate = tpl;
        await updateGuildConfig(guildId, payload);
        return 'Ajustes de canales de voz guardados con éxito';
    });

    bindButton('save-downloads-btn', 'Guardando...', async () => {
        await updateGuildConfig(guildId, { downloadsEnabled: checked('downloadsEnabled') });
        return 'Ajustes de descargas guardados con éxito';
    });

    bindButton('save-birthday-btn', 'Guardando...', async () => {
        const payload = { enabled: checked('birthdayEnabled') };
        payload.channelId = readOptional('birthdayChannelId') ?? '';
        const hour = readOptionalNumber('birthdayHourUtc');
        if (hour !== null) payload.hourUtc = Math.max(0, Math.min(23, hour));
        const msg = readOptional('birthdayMessage');
        if (msg !== null) payload.message = msg;
        await updateBirthdayConfig(guildId, payload);
        return 'Ajustes de cumpleaños guardados con éxito';
    });
}

function bindButton(id, loadingText, action, skipErrorPrefix = false) {
    const btn = document.getElementById(id);
    if (!btn) return;
    btn.addEventListener('click', async () => {
        const done = withLoading(btn, loadingText);
        try {
            const okMessage = await action();
            showToast(okMessage || 'Guardado con éxito');
        } catch (error) {
            if (error.status === 401) {
                showToast('La clave de API del panel es incorrecta o falta. Revísala en la sección General.', 'error');
            } else if (skipErrorPrefix) {
                showToast('No se pudo completar la acción.', 'error');
            } else {
                showToast('Error al guardar los ajustes.', 'error');
            }
            console.error(error);
        } finally {
            done();
        }
    });
}

function checked(id) {
    return document.getElementById(id)?.checked ?? false;
}

function setIfPresent(payload, key, value) {
    if (value !== null) payload[key] = value;
}

window.addEventListener('DOMContentLoaded', init);
