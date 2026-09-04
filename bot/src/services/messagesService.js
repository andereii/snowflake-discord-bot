import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import db from './database.js';
import { formatLanguageFallbackNotice } from './fallbackNotices.js';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const localesDir = path.join(__dirname, '..', 'locales');

const locales = ['en', 'es', 'pt'];
const dictionaries = {};

// Load dictionaries
function loadDictionaries() {
    for (const lang of locales) {
        const filePath = path.join(localesDir, `messages.${lang}.json`);
        if (fs.existsSync(filePath)) {
            try {
                const raw = fs.readFileSync(filePath, 'utf8');
                const clean = raw.replace(/^\s*\/\/.*$/gm, '');
                const parsed = JSON.parse(clean);
                dictionaries[lang] = parsed[lang] || parsed;
            } catch (err) {
                console.error(`[MessagesService] Error loading messages.${lang}.json:`, err);
                dictionaries[lang] = {};
            }
        } else {
            dictionaries[lang] = {};
        }
    }
}

loadDictionaries();

function getValueByPath(obj, keyPath) {
    if (!obj) return null;
    const parts = keyPath.split(/[:.]/);
    let current = obj;
    for (const part of parts) {
        if (current === undefined || current === null || typeof current !== 'object') {
            return null;
        }
        current = current[part];
    }
    return typeof current === 'string' ? current : null;
}

export class MessagesService {
    static globalFallbackListener = null;

    static setFallbackListener(fn) {
        MessagesService.globalFallbackListener = fn;
    }

    /**
     * Get active language code for a guild (en, es, pt)
     */
    static locale(guildId) {
        if (!guildId) return 'en';
        try {
            const row = db.prepare('SELECT Language FROM GuildConfigs WHERE GuildId = ?').get(String(guildId));
            const lang = row?.Language?.toLowerCase();
            if (lang === 'es' || lang === 'pt' || lang === 'en') return lang;
            return 'en';
        } catch {
            return 'en';
        }
    }

    /**
     * Get a localized message for a guild or language code, with fallback to English
     * @param {string|number|null} guildIdOrLocale
     * @param {string} key e.g. "Moderacion:Exito:Silencio" or "Ping:Respuesta"
     * @param {Object|Array} [placeholders] e.g. { usuario: "Alex", latencia: 42 }
     * @param {Object} [options]
     * @param {Function} [options.onFallback] Callback (fallbackInfo) => void
     * @param {import('discord.js').Interaction} [options.interaction] Interaction to send ephemeral alert if fell back
     * @returns {string}
     */
    static get(guildIdOrLocale, key, placeholders = {}, options = {}) {
        let lang = 'en';
        if (typeof guildIdOrLocale === 'string' && (guildIdOrLocale === 'es' || guildIdOrLocale === 'pt' || guildIdOrLocale === 'en')) {
            lang = guildIdOrLocale;
        } else if (guildIdOrLocale) {
            lang = MessagesService.locale(guildIdOrLocale);
        }

        let fellBack = false;
        let fromLang = lang;
        let toLang = lang;

        // Try selected language
        let text = getValueByPath(dictionaries[lang], key);

        // Fallback to English
        if (!text && lang !== 'en') {
            text = getValueByPath(dictionaries['en'], key);
            if (text) {
                fellBack = true;
                toLang = 'en';
            }
        }

        // Fallback to Spanish if English is missing
        if (!text && lang !== 'es') {
            text = getValueByPath(dictionaries['es'], key);
            if (text) {
                fellBack = true;
                toLang = 'es';
            }
        }

        if (fellBack) {
            const fallbackInfo = {
                type: 'language_fallback',
                key,
                from: fromLang,
                to: toLang,
                reason: `Key '${key}' not found in language '${fromLang}'`
            };

            if (options.onFallback) {
                options.onFallback(fallbackInfo);
            }
            if (options.interaction && typeof options.interaction.followUp === 'function') {
                const notice = formatLanguageFallbackNotice(fromLang, fallbackInfo);
                options.interaction.followUp({
                    content: notice,
                    ephemeral: true
                }).catch(() => {});
            }
            if (MessagesService.globalFallbackListener) {
                MessagesService.globalFallbackListener(fallbackInfo);
            }
        }

        if (!text) {
            return `⚠️ Message not found: \`${key}\``;
        }

        // Replace placeholders {key} -> value
        if (Array.isArray(placeholders)) {
            for (const [k, v] of placeholders) {
                text = text.replaceAll(`{${k}}`, v !== undefined && v !== null ? String(v) : '');
            }
        } else if (placeholders && typeof placeholders === 'object') {
            for (const [k, v] of Object.entries(placeholders)) {
                text = text.replaceAll(`{${k}}`, v !== undefined && v !== null ? String(v) : '');
            }
        }

        return text;
    }

    /**
     * Get English string directly
     */
    static en(key, placeholders = {}) {
        return MessagesService.get('en', key, placeholders);
    }

    /**
     * Reload messages from disk (hot reload)
     */
    static reload() {
        loadDictionaries();
    }
}

export default MessagesService;
