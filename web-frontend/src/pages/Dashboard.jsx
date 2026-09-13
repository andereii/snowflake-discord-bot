import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../auth';
import { useI18n } from '../i18n';
import {
    fetchGuilds,
    guildIconUrl,
    guildBannerUrl,
    botInviteUrl,
    fallbackAvatar
} from '../api';

export default function Dashboard() {
    const navigate = useNavigate();
    const { user, status, refresh } = useAuth();
    const { t } = useI18n();
    const [guilds, setGuilds] = useState([]);
    const [loading, setLoading] = useState(true);
    const [inviteOpen, setInviteOpen] = useState(false);

    useEffect(() => {
        const params = new URLSearchParams(window.location.search);
        if (params.has('login')) {
            window.history.replaceState({}, '', '/');
            refresh();
        }
    }, [refresh]);

    useEffect(() => {
        if (status !== 'ready') return undefined;
        if (!user) {
            setGuilds([]);
            setLoading(false);
            return undefined;
        }

        let cancelled = false;
        let lastLoad = 0;
        async function loadGuilds(force = false) {
            if (!force && Date.now() - lastLoad < 20_000) return;
            lastLoad = Date.now();
            try {
                const list = await fetchGuilds();
                if (!cancelled) setGuilds(list);
            } catch {
                // keep the last successful list
            } finally {
                if (!cancelled) setLoading(false);
            }
        }
        loadGuilds(true);
        const onVisible = () => {
            if (document.visibilityState === 'visible') loadGuilds(false);
        };
        document.addEventListener('visibilitychange', onVisible);
        return () => {
            cancelled = true;
            document.removeEventListener('visibilitychange', onVisible);
        };
    }, [user, status]);

    const inviteGuilds = guilds.filter((g) => !g.hasBot);

    return (
        <>
            <main className="dashboard-container">
                {(loading || status === 'loading') && <div className="page-status">{t('loading')}</div>}

                {status === 'ready' && !user && (
                    <div className="login-prompt">
                        <h1>{t('loginTitle')}</h1>
                        <p>{t('loginBody')}</p>
                        <a href="/api/auth/discord" className="btn-discord">
                            <i className="fa-brands fa-discord" /> {t('loginDiscord')}
                        </a>
                    </div>
                )}

                {status === 'ready' && user && !loading && (
                    <div className="view-section">
                        <div className="servers-grid">
                            {guilds.map((guild) => (
                                <article key={guild.id} className="server-card">
                                    <div
                                        className="server-banner"
                                        style={guildBannerUrl(guild)
                                            ? { backgroundImage: `url(${guildBannerUrl(guild)})` }
                                            : undefined}
                                    />
                                    <img
                                        className="server-icon"
                                        src={guildIconUrl(guild)}
                                        alt=""
                                        onError={(e) => { e.currentTarget.src = fallbackAvatar; }}
                                    />
                                    <div className="server-info">
                                        <h3 className="server-name">{guild.name}</h3>
                                        {guild.hasBot ? (
                                            <button
                                                type="button"
                                                className="btn-manage"
                                                onClick={() => navigate(`/manage/${guild.id}`, { state: { guild } })}
                                            >
                                                {t('configure')} <i className="fa-solid fa-gear" />
                                            </button>
                                        ) : (
                                            <a
                                                className="btn-manage btn-invite"
                                                href={botInviteUrl(guild.id)}
                                            >
                                                <i className="fa-solid fa-plus" /> {t('add')}
                                            </a>
                                        )}
                                    </div>
                                </article>
                            ))}

                            <button
                                type="button"
                                className="server-card add-server-card"
                                onClick={() => setInviteOpen(true)}
                            >
                                <i className="fa-solid fa-plus" />
                                <h3>{t('addServer')}</h3>
                            </button>
                        </div>
                    </div>
                )}
            </main>

            {inviteOpen && (
                <div className="modal-overlay show" onClick={(e) => { if (e.target === e.currentTarget) setInviteOpen(false); }}>
                    <div className="modal-content">
                        <div className="modal-header">
                            <h2>{t('addToServer')}</h2>
                            <button type="button" className="close-modal" onClick={() => setInviteOpen(false)}>
                                <i className="fa-solid fa-xmark" />
                            </button>
                        </div>
                        <div className="modal-body servers-list">
                            {inviteGuilds.length === 0 ? (
                                <p style={{ textAlign: 'center', color: 'var(--color-text-muted)' }}>
                                    {t('noInviteGuilds')}
                                </p>
                            ) : inviteGuilds.map((guild) => (
                                <div key={guild.id} className="invite-item">
                                    <div className="invite-item-info">
                                        <img className="invite-icon" src={guildIconUrl(guild)} alt="" />
                                        <span className="invite-name">{guild.name}</span>
                                    </div>
                                    <a href={botInviteUrl(guild.id)} className="btn-manage btn-invite">
                                        {t('addBot')} <i className="fa-solid fa-plus" />
                                    </a>
                                </div>
                            ))}
                        </div>
                    </div>
                </div>
            )}
        </>
    );
}
