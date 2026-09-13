import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth';
import { useI18n } from '../i18n';
import LanguageSelect from './LanguageSelect';
import UserPill from './UserPill';

export default function Navbar({ showBack = false }) {
    const { user, status, logout } = useAuth();
    const { t } = useI18n();
    const navigate = useNavigate();

    return (
        <nav className="navbar">
            <div className="navbar-start">
                {showBack && (
                    <Link to="/" className="navbar-back" aria-label={t('back')} title={t('back')}>
                        <i className="fa-solid fa-arrow-left" />
                    </Link>
                )}
                <Link to="/" className="navbar-brand">
                    <i className="fa-solid fa-snowflake" /> Snowflake
                </Link>
            </div>
            <div className="navbar-actions">
                <LanguageSelect />
                <button type="button" className="nav-chip nav-chip-icon" title={t('themeSoon')} aria-label={t('themeSoon')}>
                    <i className="fa-solid fa-moon" />
                </button>
                <button type="button" className="nav-chip nav-chip-premium" title={t('premiumSoon')}>
                    <i className="fa-solid fa-crown" />
                    {t('premium')}
                </button>
                {status === 'loading' || user ? (
                    user ? (
                        <UserPill
                            user={user}
                            onLogout={async () => {
                                await logout();
                                navigate('/');
                            }}
                        />
                    ) : (
                        <div className="user-pill user-pill-placeholder" aria-hidden="true" />
                    )
                ) : (
                    <a href="/api/auth/discord" className="btn-discord">
                        <i className="fa-brands fa-discord" /> {t('loginDiscord')}
                    </a>
                )}
            </div>
        </nav>
    );
}
