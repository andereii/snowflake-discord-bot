export default function Toast({ toast }) {
    if (!toast) return null;
    return (
        <div className={`toast show${toast.error ? ' error' : ''}`}>
            <i className={`fa-solid ${toast.error ? 'fa-circle-exclamation' : 'fa-circle-check'}`} />
            <span>{toast.message}</span>
        </div>
    );
}
