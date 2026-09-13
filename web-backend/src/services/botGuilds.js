const DISCORD_API = 'https://discord.com/api/v10';
const BOT_API = process.env.BOT_API_URL || 'http://127.0.0.1:8080';
const INTERNAL_SECRET = process.env.BOT_INTERNAL_SECRET || '';
const TTL_MS = 5 * 60 * 1000;

let cache = { ids: new Set(), at: 0 };
let inflight = null;

export async function fetchBotGuildIds() {
    if (cache.ids.size && Date.now() - cache.at < TTL_MS) return cache.ids;
    if (inflight) return inflight;
    inflight = loadBotGuilds().finally(() => { inflight = null; });
    return inflight;
}

function remember(ids) {
    cache = { ids, at: Date.now() };
    return ids;
}

async function loadBotGuilds() {
    try {
        const headers = {};
        if (INTERNAL_SECRET) headers['x-internal-token'] = INTERNAL_SECRET;
        const response = await fetch(`${BOT_API}/api/bot/guilds`, {
            headers,
            signal: AbortSignal.timeout(2000)
        });
        if (response.ok) {
            const data = await response.json();
            const ids = new Set((data.ids || []).map(String));
            return remember(ids);
        }
    } catch {
        // bot internal API not up; fall through
    }

    if (cache.ids.size) return cache.ids;

    const token = process.env.DISCORD_TOKEN;
    if (!token) {
        console.warn('[web-backend] DISCORD_TOKEN missing; cannot detect bot guilds');
        return cache.ids;
    }

    const ids = new Set();
    let after;
    for (let i = 0; i < 10; i++) {
        const url = new URL(`${DISCORD_API}/users/@me/guilds`);
        url.searchParams.set('limit', '200');
        if (after) url.searchParams.set('after', after);

        const response = await fetch(url, {
            headers: { Authorization: `Bot ${token}` }
        });

        if (response.status === 429) {
            const retry = Number(response.headers.get('retry-after')) || 1;
            console.warn(`[web-backend] bot guilds rate-limited; retry in ${retry}s`);
            if (cache.ids.size) return cache.ids;
            await new Promise((r) => setTimeout(r, Math.min(retry, 5) * 1000));
            continue;
        }

        if (!response.ok) {
            console.error('[web-backend] bot guilds fetch failed:', response.status);
            return cache.ids.size ? cache.ids : ids;
        }

        const batch = await response.json();
        if (!Array.isArray(batch) || batch.length === 0) break;
        for (const guild of batch) ids.add(String(guild.id));
        if (batch.length < 200) break;
        after = batch[batch.length - 1].id;
    }

    return remember(ids);
}
