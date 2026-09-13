import { useEffect, useMemo, useState } from 'react';
import { useLocation, useParams } from 'react-router-dom';
import { api, API_KEY_STORAGE, fallbackAvatar, guildIconUrl } from '../api';
import { useI18n } from '../i18n';
import SettingCard, { SaveBar, Toggle } from '../components/SettingCard';
import Toast from '../components/Toast';

const SECTIONS = [
    { id: 'inicio', icon: 'fa-house', labelKey: 'navHome' },
    { id: 'general', icon: 'fa-gear', labelKey: 'navGeneral' },
    { id: 'moderacion', icon: 'fa-shield-halved', labelKey: 'navModeration' },
    { id: 'bienvenida', icon: 'fa-handshake', labelKey: 'navWelcome' },
    { id: 'musica', icon: 'fa-music', labelKey: 'navMusic' },
    { id: 'ia', icon: 'fa-robot', labelKey: 'navAi' },
    { id: 'conteo', icon: 'fa-1', labelKey: 'navCounting' },
    { id: 'youtube', icon: 'fa-brands fa-youtube', labelKey: 'navYoutube' },
    { id: 'voces', icon: 'fa-microphone-lines', labelKey: 'navVoice' },
    { id: 'descargas', icon: 'fa-download', labelKey: 'navDownloads' },
    { id: 'cumple', icon: 'fa-cake-candles', labelKey: 'navBirthday' },
    { id: 'bloqueos', icon: 'fa-lock', labelKey: 'navLocks' }
];

function iconClass(icon) {
    return icon.startsWith('fa-brands') ? icon : `fa-solid ${icon}`;
}

export default function Manage() {
    const { guildId } = useParams();
    const location = useLocation();
    const { t } = useI18n();
    const guildFromState = location.state?.guild;

    const [section, setSection] = useState('inicio');
    const [config, setConfig] = useState(null);
    const [form, setForm] = useState({});
    const [stats, setStats] = useState(null);
    const [members, setMembers] = useState([]);
    const [roles, setRoles] = useState([]);
    const [toast, setToast] = useState(null);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        let cancelled = false;
        (async () => {
            try {
                const { data } = await api.get(`/api/guilds/${guildId}/config`);
                if (cancelled) return;
                setConfig(data);
                setForm(formFromConfig(data));
            } catch (err) {
                if (!cancelled) showToast(err.response?.status === 401 ? t('badApiKey') : t('loadError'), true);
            } finally {
                if (!cancelled) setLoading(false);
            }
        })();

        api.get(`/api/guilds/${guildId}/stats`).then((r) => setStats(r.data)).catch(() => {});
        api.get(`/api/guilds/${guildId}/members`).then((r) => setMembers(r.data.members || [])).catch(() => {});
        api.get(`/api/guilds/${guildId}/roles`).then((r) => setRoles(r.data.roles || [])).catch(() => {});

        return () => { cancelled = true; };
    }, [guildId]);

    function showToast(message, error = false) {
        setToast({ message, error });
        setTimeout(() => setToast(null), 2800);
    }

    const patch = (key, value) => setForm((prev) => ({ ...prev, [key]: value }));

    async function save(payload, endpoint = '') {
        try {
            await api.post(`/api/guilds/${guildId}/config${endpoint}`, payload);
            const { data } = await api.get(`/api/guilds/${guildId}/config`);
            setConfig(data);
            setForm(formFromConfig(data));
            showToast(t('saved'));
        } catch (err) {
            showToast(err.response?.status === 401 ? t('badApiKey') : t('saveError'), true);
        }
    }

    const guildName = guildFromState?.name || stats?.name || t('serverFallback', { id: guildId });
    const iconSrc = guildFromState ? guildIconUrl(guildFromState) : (stats?.iconUrl || fallbackAvatar);

    const blocked = config?.blockedChannels || [];

    const content = useMemo(() => {
        if (!config && section !== 'inicio') {
            return <div className="page-status">{t('configMissing')}</div>;
        }
        switch (section) {
            case 'inicio':
                return (
                    <HomeSection
                        t={t}
                        guildName={guildName}
                        iconSrc={iconSrc}
                        stats={stats}
                        members={members}
                        roles={roles}
                        onGoto={setSection}
                    />
                );
            case 'general':
                return (
                    <Section title={t('generalTitle')} desc={t('generalDesc')}>
                        <SettingCard title={t('generalLang')} description={t('generalLangDesc')}>
                            <select className="form-input" value={form.language} onChange={(e) => patch('language', e.target.value)}>
                                <option value="en">English</option>
                                <option value="es">Español</option>
                                <option value="pt">Português</option>
                            </select>
                        </SettingCard>
                        <SettingCard title={t('generalApi')} description={t('generalApiDesc')}>
                            <input
                                type="password"
                                className="form-input"
                                value={form.apiKey}
                                onChange={(e) => {
                                    patch('apiKey', e.target.value);
                                    localStorage.setItem(API_KEY_STORAGE, e.target.value);
                                }}
                                placeholder={t('optional')}
                                autoComplete="off"
                            />
                        </SettingCard>
                        <SaveBar onSave={() => save({ language: form.language })} />
                    </Section>
                );
            case 'moderacion':
                return (
                    <Section title={t('modTitle')} desc={t('modDesc')}>
                        <SettingCard title={t('modLog')} description={t('modLogDesc')}>
                            <input className="form-input" value={form.modLogChannelId} onChange={(e) => patch('modLogChannelId', e.target.value)} placeholder={t('channelId')} />
                        </SettingCard>
                        <SaveBar onSave={() => save({ modLogChannelId: form.modLogChannelId || null })} />
                    </Section>
                );
            case 'bienvenida':
                return (
                    <Section title={t('welcomeTitle')} desc={t('welcomeDesc')}>
                        <SettingCard title={t('welcomeChannel')} description={t('welcomeChannelDesc')}>
                            <input className="form-input" value={form.welcomeChannelId} onChange={(e) => patch('welcomeChannelId', e.target.value)} placeholder={t('channelId')} />
                        </SettingCard>
                        <SettingCard title={t('welcomeMessage')} description={t('welcomeMessageDesc')}>
                            <input className="form-input" value={form.welcomeMessage} onChange={(e) => patch('welcomeMessage', e.target.value)} placeholder="¡Bienvenido {usuario}!" />
                        </SettingCard>
                        <SaveBar onSave={() => save({ welcomeChannelId: form.welcomeChannelId || null, welcomeMessage: form.welcomeMessage || null })} />
                    </Section>
                );
            case 'musica':
                return (
                    <Section title={t('musicTitle')} desc={t('musicDesc')}>
                        <SettingCard title={t('volume')} description={t('volumeDesc')}>
                            <input type="number" min="0" max="100" className="form-input" value={form.volume} onChange={(e) => patch('volume', e.target.value)} placeholder="100" />
                        </SettingCard>
                        <SettingCard title={t('djRole')} description={t('djRoleDesc')}>
                            <input className="form-input" value={form.djRoleId} onChange={(e) => patch('djRoleId', e.target.value)} placeholder={t('roleId')} />
                        </SettingCard>
                        <SaveBar onSave={() => save({
                            volume: form.volume === '' ? null : Number(form.volume),
                            djRoleId: form.djRoleId || null
                        })} />
                    </Section>
                );
            case 'ia':
                return (
                    <Section title={t('aiTitle')} desc={t('aiDesc')}>
                        <SettingCard title={t('aiChat')} description={t('aiChatDesc')}>
                            <Toggle checked={form.aiChatEnabled} onChange={(v) => patch('aiChatEnabled', v)} />
                        </SettingCard>
                        <SettingCard title={t('aiMentions')} description={t('aiMentionsDesc')}>
                            <Toggle checked={form.aiMentionsEnabled} onChange={(v) => patch('aiMentionsEnabled', v)} />
                        </SettingCard>
                        <SettingCard title={t('aiSpontaneous')} description={t('aiSpontaneousDesc')}>
                            <Toggle checked={form.aiSpontaneousEnabled} onChange={(v) => patch('aiSpontaneousEnabled', v)} />
                        </SettingCard>
                        <SettingCard title={t('aiSearch')} description={t('aiSearchDesc')}>
                            <Toggle checked={form.aiWebSearchEnabled} onChange={(v) => patch('aiWebSearchEnabled', v)} />
                        </SettingCard>
                        <SettingCard title={t('aiCommands')} description={t('aiCommandsDesc')}>
                            <Toggle checked={form.aiCommandsEnabled} onChange={(v) => patch('aiCommandsEnabled', v)} />
                        </SettingCard>
                        <SaveBar onSave={() => save({
                            aiChatEnabled: form.aiChatEnabled,
                            aiMentionsEnabled: form.aiMentionsEnabled,
                            aiSpontaneousEnabled: form.aiSpontaneousEnabled,
                            aiWebSearchEnabled: form.aiWebSearchEnabled,
                            aiCommandsEnabled: form.aiCommandsEnabled
                        })} />
                    </Section>
                );
            case 'conteo':
                return (
                    <Section title={t('countTitle')} desc={t('countDesc')}>
                        <SettingCard title={t('countChannel')} description={t('countChannelDesc')}>
                            <input className="form-input" value={form.countingChannelId} onChange={(e) => patch('countingChannelId', e.target.value)} placeholder={t('channelId')} />
                        </SettingCard>
                        <SettingCard title={t('countBase')} description={t('countBaseDesc')}>
                            <select className="form-input" value={form.countingBase} onChange={(e) => patch('countingBase', e.target.value)}>
                                <option value="Decimal">Decimal</option>
                                <option value="Binario">Binario</option>
                                <option value="Octal">Octal</option>
                                <option value="Hexadecimal">Hexadecimal</option>
                            </select>
                        </SettingCard>
                        <SettingCard title={t('countGoal')} description={t('countGoalDesc')}>
                            <input type="number" min="1" className="form-input" value={form.countingGoal} onChange={(e) => patch('countingGoal', e.target.value)} placeholder={t('noGoal')} />
                        </SettingCard>
                        <SettingCard title={t('countChances')} description={t('countChancesDesc')}>
                            <input type="number" min="0" max="10" className="form-input" value={form.countingChances} onChange={(e) => patch('countingChances', e.target.value)} placeholder="0" />
                        </SettingCard>
                        <SettingCard title={t('countOk')} description={t('countOkDesc')}>
                            <input className="form-input" value={form.countingEmojiCorrect} onChange={(e) => patch('countingEmojiCorrect', e.target.value)} placeholder="✅" />
                        </SettingCard>
                        <SettingCard title={t('countBad')} description={t('countBadDesc')}>
                            <input className="form-input" value={form.countingEmojiIncorrect} onChange={(e) => patch('countingEmojiIncorrect', e.target.value)} placeholder="❌" />
                        </SettingCard>
                        <SettingCard title={t('countRecord')} description={t('countRecordDesc')}>
                            <input className="form-input" value={form.countingEmojiRecord} onChange={(e) => patch('countingEmojiRecord', e.target.value)} placeholder="🎉" />
                        </SettingCard>
                        <SettingCard title={t('countLose')} description={t('countLoseDesc')}>
                            <input className="form-input" value={form.countingLoseMessage} onChange={(e) => patch('countingLoseMessage', e.target.value)} placeholder={t('defaultMessage')} />
                        </SettingCard>
                        <SaveBar onSave={() => save({
                            channelId: form.countingChannelId || null,
                            base: form.countingBase,
                            goal: form.countingGoal === '' ? null : Number(form.countingGoal),
                            extraChancesPerDay: form.countingChances === '' ? 0 : Number(form.countingChances),
                            emojiCorrect: form.countingEmojiCorrect || null,
                            emojiIncorrect: form.countingEmojiIncorrect || null,
                            emojiRecord: form.countingEmojiRecord || null,
                            loseMessage: form.countingLoseMessage || null
                        }, '/counting')} />
                    </Section>
                );
            case 'youtube':
                return (
                    <Section title={t('ytTitle')} desc={t('ytDesc')}>
                        <SettingCard title={t('ytChannel')} description={t('ytChannelDesc')}>
                            <input className="form-input" value={form.ytChannelId} onChange={(e) => patch('ytChannelId', e.target.value)} placeholder={t('ytChannel')} />
                        </SettingCard>
                        <SettingCard title={t('ytName')} description={t('ytNameDesc')}>
                            <input className="form-input" value={form.ytChannelName} onChange={(e) => patch('ytChannelName', e.target.value)} placeholder={t('ytName')} />
                        </SettingCard>
                        <SettingCard title={t('ytNotify')} description={t('ytNotifyDesc')}>
                            <input className="form-input" value={form.ytNotifyChannelId} onChange={(e) => patch('ytNotifyChannelId', e.target.value)} placeholder={t('channelId')} />
                        </SettingCard>
                        <SettingCard title={t('ytRole')} description={t('ytRoleDesc')}>
                            <input className="form-input" value={form.ytNotifyRoleId} onChange={(e) => patch('ytNotifyRoleId', e.target.value)} placeholder={t('roleId')} />
                        </SettingCard>
                        <SettingCard title={t('ytMessage')} description={t('ytMessageDesc')}>
                            <input className="form-input" value={form.ytCustomMessage} onChange={(e) => patch('ytCustomMessage', e.target.value)} placeholder="{canal}" />
                        </SettingCard>
                        <SaveBar
                            extra={(
                                <button
                                    type="button"
                                    className="btn-manage"
                                    style={{ color: 'var(--color-danger)', background: 'var(--color-danger-bg)' }}
                                    onClick={async () => {
                                        try {
                                            await api.delete(`/api/guilds/${guildId}/config/youtube`);
                                            const { data } = await api.get(`/api/guilds/${guildId}/config`);
                                            setConfig(data);
                                            setForm(formFromConfig(data));
                                            showToast(t('ytRemoved'));
                                        } catch {
                                            showToast(t('ytRemoveError'), true);
                                        }
                                    }}
                                >
                                    <i className="fa-solid fa-trash" /> {t('ytRemove')}
                                </button>
                            )}
                            onSave={() => save({
                                ytChannelId: form.ytChannelId || null,
                                ytChannelName: form.ytChannelName || null,
                                notifyChannelId: form.ytNotifyChannelId || null,
                                notifyRoleId: form.ytNotifyRoleId || null,
                                customMessage: form.ytCustomMessage || null
                            }, '/youtube')}
                        />
                    </Section>
                );
            case 'voces':
                return (
                    <Section title={t('voiceTitle')} desc={t('voiceDesc')}>
                        <SettingCard title={t('voiceHub')} description={t('voiceHubDesc')}>
                            <input className="form-input" value={form.hubChannelId} onChange={(e) => patch('hubChannelId', e.target.value)} placeholder={t('channelId')} />
                        </SettingCard>
                        <SettingCard title={t('voiceTemplate')} description={t('voiceTemplateDesc')}>
                            <input className="form-input" value={form.tempChannelNameTemplate} onChange={(e) => patch('tempChannelNameTemplate', e.target.value)} placeholder="{usuario}" />
                        </SettingCard>
                        <SaveBar onSave={() => save({
                            hubChannelId: form.hubChannelId || null,
                            tempChannelNameTemplate: form.tempChannelNameTemplate || null
                        })} />
                    </Section>
                );
            case 'descargas':
                return (
                    <Section title={t('dlTitle')} desc={t('dlDesc')}>
                        <SettingCard title={t('dlToggle')} description={t('dlToggleDesc')}>
                            <Toggle checked={form.downloadsEnabled} onChange={(v) => patch('downloadsEnabled', v)} />
                        </SettingCard>
                        <SaveBar onSave={() => save({ downloadsEnabled: form.downloadsEnabled })} />
                    </Section>
                );
            case 'cumple':
                return (
                    <Section title={t('bdTitle')} desc={t('bdDesc')}>
                        <SettingCard title={t('bdEnabled')} description={t('bdEnabledDesc')}>
                            <Toggle checked={form.birthdayEnabled} onChange={(v) => patch('birthdayEnabled', v)} />
                        </SettingCard>
                        <SettingCard title={t('bdChannel')} description={t('bdChannelDesc')}>
                            <input className="form-input" value={form.birthdayChannelId} onChange={(e) => patch('birthdayChannelId', e.target.value)} placeholder={t('channelId')} />
                        </SettingCard>
                        <SettingCard title={t('bdHour')} description={t('bdHourDesc')}>
                            <input type="number" min="0" max="23" className="form-input" value={form.birthdayHourUtc} onChange={(e) => patch('birthdayHourUtc', e.target.value)} placeholder="12" />
                        </SettingCard>
                        <SettingCard title={t('bdMessage')} description={t('bdMessageDesc')}>
                            <input className="form-input" value={form.birthdayMessage} onChange={(e) => patch('birthdayMessage', e.target.value)} placeholder="{usuario}" />
                        </SettingCard>
                        <SaveBar onSave={() => save({
                            enabled: form.birthdayEnabled,
                            channelId: form.birthdayChannelId || null,
                            hourUtc: form.birthdayHourUtc === '' ? 12 : Number(form.birthdayHourUtc),
                            message: form.birthdayMessage || ''
                        }, '/birthday')} />
                    </Section>
                );
            case 'bloqueos':
                return (
                    <Section title={t('lockTitle')} desc={t('lockDesc')}>
                        <SettingCard title={t('lockList')} description={t('lockListDesc')}>
                            <ul className="blocked-list">
                                {blocked.length === 0
                                    ? <li style={{ color: 'var(--color-text-muted)' }}>{t('lockNone')}</li>
                                    : blocked.map((id) => <li key={id} className="blocked-item">{id}</li>)}
                            </ul>
                        </SettingCard>
                    </Section>
                );
            default:
                return null;
        }
    }, [section, config, form, stats, members, roles, guildName, iconSrc, blocked, guildId, t]);

    return (
        <div className="manage-shell">
                <aside className="sidebar">
                    <nav className="sidebar-menu">
                        {SECTIONS.map((item) => (
                            <button
                                key={item.id}
                                type="button"
                                className={`sidebar-menu-item${section === item.id ? ' active' : ''}`}
                                onClick={() => setSection(item.id)}
                            >
                                <i className={iconClass(item.icon)} /> {t(item.labelKey)}
                            </button>
                        ))}
                    </nav>
                </aside>
                <main className="manage-container">
                    {loading ? <div className="page-status">{t('loadingConfig')}</div> : content}
                </main>
                <Toast toast={toast} />
            </div>
    );
}

function formFromConfig(data) {
    return {
        language: data.language || 'en',
        apiKey: localStorage.getItem(API_KEY_STORAGE) || '',
        modLogChannelId: data.moderation?.logChannelId || '',
        welcomeChannelId: data.welcome?.channelId || '',
        welcomeMessage: data.welcome?.message || '',
        volume: data.music?.volume ?? '',
        djRoleId: data.music?.djRoleId || '',
        aiChatEnabled: data.ai?.chatEnabled ?? true,
        aiMentionsEnabled: data.ai?.mentionsEnabled ?? false,
        aiSpontaneousEnabled: data.ai?.spontaneousEnabled ?? false,
        aiWebSearchEnabled: data.ai?.webSearchEnabled ?? true,
        aiCommandsEnabled: data.ai?.commandsEnabled ?? true,
        countingChannelId: data.counting?.channelId || '',
        countingBase: data.counting?.base || 'Decimal',
        countingGoal: data.counting?.goal ?? '',
        countingChances: data.counting?.extraChancesPerDay ?? 0,
        countingEmojiCorrect: data.counting?.emojiCorrect || '',
        countingEmojiIncorrect: data.counting?.emojiIncorrect || '',
        countingEmojiRecord: data.counting?.emojiRecord || '',
        countingLoseMessage: data.counting?.loseMessage || '',
        ytChannelId: data.youtube?.channelId || '',
        ytChannelName: data.youtube?.channelName || '',
        ytNotifyChannelId: data.youtube?.notifyChannelId || '',
        ytNotifyRoleId: data.youtube?.notifyRoleId || '',
        ytCustomMessage: data.youtube?.customMessage || '',
        hubChannelId: data.voice?.hubChannelId || '',
        tempChannelNameTemplate: data.voice?.tempChannelNameTemplate || '',
        downloadsEnabled: data.downloads?.enabled ?? true,
        birthdayEnabled: data.birthday?.enabled ?? false,
        birthdayChannelId: data.birthday?.channelId || '',
        birthdayHourUtc: data.birthday?.hourUtc ?? 12,
        birthdayMessage: data.birthday?.message || ''
    };
}

function Section({ title, desc, children }) {
    return (
        <section className="settings-section active">
            <h2>{title}</h2>
            <p>{desc}</p>
            {children}
        </section>
    );
}

function HomeSection({ t, guildName, iconSrc, stats, members, roles, onGoto }) {
    return (
        <section className="settings-section active">
            <div className="home-widget">
                <div className="home-main">
                    <div className="home-badge"><i className="fa-solid fa-wand-magic-sparkles" /> {t('homeBadge')}</div>
                    <div className="home-header">
                        <img src={iconSrc} alt="" className="home-icon" onError={(e) => { e.currentTarget.src = fallbackAvatar; }} />
                        <h2>{guildName}</h2>
                    </div>
                    <p className="home-desc">{t('homeDesc')}</p>
                    <div className="home-actions">
                        <button type="button" className="btn-manage" onClick={() => onGoto('general')}><i className="fa-solid fa-gear" /> {t('homeSettings')}</button>
                        <button type="button" className="btn-manage" onClick={() => onGoto('bienvenida')}><i className="fa-solid fa-handshake" /> {t('homeWelcomes')}</button>
                        <button type="button" className="btn-manage" onClick={() => onGoto('ia')}><i className="fa-solid fa-robot" /> {t('homeAi')}</button>
                        <button type="button" className="btn-manage" onClick={() => onGoto('musica')}><i className="fa-solid fa-music" /> {t('homeMusic')}</button>
                    </div>
                </div>
                <div className="home-stats">
                    <HomeStat icon="fa-users" value={stats?.memberCount ?? '–'} label={t('members')} />
                    <HomeStat icon="fa-hashtag" value={stats?.channelCount ?? '–'} label={t('channels')} />
                    <HomeStat icon="fa-shield-halved" value={stats?.roleCount ?? (roles.length || '–')} label={t('roles')} />
                </div>
            </div>
            <div className="home-columns">
                <div className="home-column">
                    <h3><i className="fa-solid fa-users" /> {t('users')}</h3>
                    <ul className="home-list">
                        {members.length === 0
                            ? <li className="home-list-empty">{t('noMembers')}</li>
                            : members.slice(0, 20).map((m) => (
                                <li key={m.id}>
                                    <img className="home-avatar" src={m.avatarUrl || fallbackAvatar} alt="" />
                                    {m.displayName || m.username || m.id}
                                </li>
                            ))}
                    </ul>
                </div>
                <div className="home-column">
                    <h3><i className="fa-solid fa-shield-halved" /> {t('roles')}</h3>
                    <ul className="home-list">
                        {roles.length === 0
                            ? <li className="home-list-empty">{t('noRoles')}</li>
                            : roles.slice(0, 20).map((r) => (
                                <li key={r.id}>
                                    <span className="home-role-dot" style={{ background: r.color || 'var(--color-border)' }} />
                                    {r.name}
                                    {r.memberCount != null && <span className="home-role-count">{r.memberCount}</span>}
                                </li>
                            ))}
                    </ul>
                </div>
            </div>
        </section>
    );
}

function HomeStat({ icon, value, label }) {
    return (
        <div className="home-stat">
            <div className="home-stat-icon"><i className={`fa-solid ${icon}`} /></div>
            <div className="home-stat-value">{value}</div>
            <div className="home-stat-label">{label}</div>
        </div>
    );
}
