import { useI18n } from '../i18n';

export default function SettingCard({ title, description, children }) {
    return (
        <div className="setting-card">
            <div className="setting-info">
                <h3>{title}</h3>
                {description ? <p>{description}</p> : null}
            </div>
            <div className="setting-control">{children}</div>
        </div>
    );
}

export function Toggle({ checked, onChange }) {
    return (
        <label className="switch">
            <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} />
            <span className="slider" />
        </label>
    );
}

export function SaveBar({ onSave, extra }) {
    const { t } = useI18n();
    return (
        <div className="save-bar" style={extra ? { justifyContent: 'space-between' } : undefined}>
            {extra}
            <button type="button" className="btn-discord" onClick={onSave}>
                {t('save')}
            </button>
        </div>
    );
}
