import { useEffect, useRef, useState } from 'react';
import { useI18n } from '../i18n';

export default function LanguageSelect() {
    const { locale, setLocale, langs } = useI18n();
    const [open, setOpen] = useState(false);
    const rootRef = useRef(null);
    const current = langs.find((l) => l.id === locale) || langs[0];

    useEffect(() => {
        if (!open) return undefined;
        const onDoc = (event) => {
            if (!rootRef.current?.contains(event.target)) setOpen(false);
        };
        document.addEventListener('click', onDoc);
        return () => document.removeEventListener('click', onDoc);
    }, [open]);

    return (
        <div className="nav-menu" ref={rootRef}>
            <button
                type="button"
                className="nav-chip"
                aria-expanded={open}
                aria-haspopup="listbox"
                onClick={(event) => {
                    event.stopPropagation();
                    setOpen((v) => !v);
                }}
            >
                <span className="lang-flag" aria-hidden="true">{current.flag}</span>
                <span className="lang-code">{current.code}</span>
                <i className="fa-solid fa-chevron-down lang-caret" />
            </button>
            <div className={`nav-menu-list${open ? ' show' : ''}`} role="listbox">
                {langs.map((lang) => (
                    <button
                        key={lang.id}
                        type="button"
                        role="option"
                        aria-selected={lang.id === locale}
                        className={`nav-menu-item${lang.id === locale ? ' active' : ''}`}
                        onClick={() => {
                            setLocale(lang.id);
                            setOpen(false);
                        }}
                    >
                        <span className="lang-flag" aria-hidden="true">{lang.flag}</span>
                        <span>{lang.name}</span>
                        <span className="lang-code-muted">{lang.code}</span>
                    </button>
                ))}
            </div>
        </div>
    );
}
