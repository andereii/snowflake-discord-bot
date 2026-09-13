import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { LANGS, MESSAGES } from './locales.js';

const STORAGE_KEY = 'snowflake_ui_locale';
const I18nContext = createContext(null);

function detectLocale() {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved && MESSAGES[saved]) return saved;
    const nav = (navigator.language || 'en').toLowerCase();
    if (nav.startsWith('es')) return 'es';
    if (nav.startsWith('pt')) return 'pt';
    return 'en';
}

export function I18nProvider({ children }) {
    const [locale, setLocaleState] = useState(detectLocale);

    const setLocale = useCallback((next) => {
        if (!MESSAGES[next]) return;
        setLocaleState(next);
        localStorage.setItem(STORAGE_KEY, next);
    }, []);

    useEffect(() => {
        document.documentElement.lang = locale;
        const names = { en: 'Dashboard - Snowflake', es: 'Dashboard - Snowflake', pt: 'Dashboard - Snowflake' };
        document.title = names[locale];
    }, [locale]);

    const t = useCallback((key, vars = {}) => {
        const table = MESSAGES[locale] || MESSAGES.en;
        let text = table[key] ?? MESSAGES.en[key] ?? key;
        for (const [name, value] of Object.entries(vars)) {
            text = text.replaceAll(`{${name}}`, String(value));
        }
        return text;
    }, [locale]);

    const value = useMemo(
        () => ({ locale, setLocale, t, langs: LANGS }),
        [locale, setLocale, t]
    );

    return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n() {
    const ctx = useContext(I18nContext);
    if (!ctx) throw new Error('useI18n must be used within I18nProvider');
    return ctx;
}
