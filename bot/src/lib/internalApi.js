import http from 'http';
import { isLocalRuntime } from './runtime.js';

function json(res, status, body) {
    res.writeHead(status, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify(body));
}

function isAuthorized(req, secret, local) {
    if (local && !secret) return true;
    if (!secret) return false;
    const header = req.headers['x-internal-token'] || '';
    return header === secret;
}

export function startInternalApi(client, { port = 8080, secret = '' } = {}) {
    const local = isLocalRuntime();
    const host = '127.0.0.1';

    const server = http.createServer((req, res) => {
        const url = req.url?.split('?')[0];
        if (req.method !== 'GET' || (url !== '/api/bot/guilds' && url !== '/internal/guilds')) {
            json(res, 404, { error: 'not found' });
            return;
        }
        if (!isAuthorized(req, secret, local)) {
            json(res, 401, { error: 'unauthorized' });
            return;
        }
        json(res, 200, { ids: [...client.guilds.cache.keys()] });
    });

    server.listen(port, host, () => {
        const auth = local && !secret ? 'open (local loopback)' : 'token required';
        console.log(`[bot] internal API http://${host}:${port} [${auth}]`);
    });
    server.on('error', (err) => {
        console.error('[bot] internal API:', err.message);
    });
    return server;
}
