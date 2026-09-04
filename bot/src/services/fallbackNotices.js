/**
 * Hardcoded tri-lingual fallback notice formatters (en, es, pt).
 * By project convention (AGENTS.md), fallback notices must be hardcoded
 * in code to ensure delivery without risking recursive lookup failures in JSON dictionaries.
 */

export function formatAiFallbackNotice(locale, { from, to, reason }) {
    const lang = locale === 'es' || locale === 'pt' ? locale : 'en';
    const templates = {
        'en': `⚠️ **AI Fallback Notice:** Could not reach **${from}** (*${reason}*). Automatically switched to **${to}** as backup.`,
        'es': `⚠️ **Aviso de Fallback (IA):** No se pudo contactar a **${from}** (*${reason}*). Se realizó un fallback automático a **${to}** como respaldo.`,
        'pt': `⚠️ **Aviso de Fallback (IA):** Não foi possível contactar **${from}** (*${reason}*). Foi feito um fallback automático para **${to}** como reserva.`
    };
    return templates[lang];
}

export function formatLanguageFallbackNotice(locale, { key, from, to }) {
    const lang = locale === 'es' || locale === 'pt' ? locale : 'en';
    const templates = {
        'en': `🌐 ⚠️ **Language Notice:** The key \`${key}\` is missing in \`${from}\`, using \`${to}\` as backup.`,
        'es': `🌐 ⚠️ **Aviso de Idioma:** La clave \`${key}\` no existe en \`${from}\` y se usó \`${to}\` como respaldo.`,
        'pt': `🌐 ⚠️ **Aviso de Idioma:** A chave \`${key}\` não existe em \`${from}\` e foi usado \`${to}\` como reserva.`
    };
    return templates[lang];
}
