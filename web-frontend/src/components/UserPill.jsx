import { useEffect, useRef, useState } from 'react';
import { userAvatarUrl } from '../api';
import { useI18n } from '../i18n';

export default function UserPill({ user, onLogout }) {
    const { t } = useI18n();
    const [open, setOpen] = useState(false);
    const rootRef = useRef(null);

    useEffect(() => {
        if (!open) return undefined;
        const onDocClick = (event) => {
            if (!rootRef.current?.contains(event.target)) setOpen(false);
        };
        document.addEventListener('click', onDocClick);
        return () => document.removeEventListener('click', onDocClick);
    }, [open]);

    return (
        <div className="user-dropdown-container" ref={rootRef}>
            <button
                type="button"
                className="user-pill"
                aria-expanded={open}
                title={t('account')}
                onClick={(event) => {
                    event.stopPropagation();
                    setOpen((value) => !value);
                }}
            >
                <span className="user-tag">@{user.username}</span>
                <img className="user-avatar" src={userAvatarUrl(user)} alt="" />
            </button>
            <div className={`user-dropdown-menu${open ? ' show' : ''}`}>
                <button type="button" className="btn-logout" onClick={onLogout}>
                    <i className="fa-solid fa-arrow-right-from-bracket" /> {t('logout')}
                </button>
            </div>
        </div>
    );
}
