function envFlag(name) {
    return (process.env[name] || '').trim();
}

export function isLocalRuntime() {
    const explicit = envFlag('SNOWFLAKE_ENV').toLowerCase();
    if (explicit === 'local' || explicit === 'development' || explicit === 'dev') return true;
    if (explicit === 'production' || explicit === 'prod' || explicit === 'vps') return false;

    if (envFlag('NODE_ENV').toLowerCase() === 'production') return false;

    if (envFlag('FLY_APP_NAME') || envFlag('RAILWAY_ENVIRONMENT')
        || envFlag('RENDER') || envFlag('KUBERNETES_SERVICE_HOST')) {
        return false;
    }

    return true;
}

export function runtimeName() {
    return isLocalRuntime() ? 'local' : 'production';
}
