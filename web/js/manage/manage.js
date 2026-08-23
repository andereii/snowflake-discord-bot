import { getGuildConfig, updateGuildConfig } from './api.js';
import { setupSidebar, showToast } from './ui.js';

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

    // 1. Obtener configuraciones actuales
    const config = await getGuildConfig(guildId);
    if (config) {
        populateForms(config);
    } else {
        showToast('Error al cargar la configuración', 'error');
    }

    // 2. Configurar botón de guardar (Moderación)
    document.getElementById('save-moderation-btn').addEventListener('click', async () => {
        const btn = document.getElementById('save-moderation-btn');
        btn.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> Guardando...';
        btn.style.pointerEvents = 'none';

        const logChannelId = document.getElementById('modLogChannelId').value.trim();

        // El payload esperado por la API (C#): { "modLogChannelId": "..." }
        // Para "quitar" el canal, se envía string vacío ""
        const payload = {
            modLogChannelId: logChannelId || ""
        };

        try {
            await updateGuildConfig(guildId, payload);
            showToast('Ajustes de moderación guardados con éxito');
        } catch (error) {
            showToast('Error al guardar los ajustes', 'error');
        } finally {
            btn.innerHTML = 'Guardar cambios';
            btn.style.pointerEvents = 'auto';
        }
    });
}

// Llenar los inputs con la info traída de la API
function populateForms(config) {
    if (config.moderation && config.moderation.logChannelId) {
        document.getElementById('modLogChannelId').value = config.moderation.logChannelId;
    }
}

window.addEventListener('DOMContentLoaded', init);
