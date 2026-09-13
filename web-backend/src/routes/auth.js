import { Router } from 'express';
import passport from 'passport';
import { fetchBotGuildIds } from '../services/botGuilds.js';

const router = Router();

router.get('/discord', passport.authenticate('discord'));

router.get('/discord/callback', (req, res, next) => {
    const redirect = process.env.FRONTEND_URL || 'http://localhost:5173';
    passport.authenticate('discord', (err, user) => {
        if (err) {
            console.error('[auth] discord callback:', err);
            return res.redirect(`${redirect}?login=error`);
        }
        if (!user) return res.redirect(`${redirect}?login=error`);
        req.logIn(user, (loginErr) => {
            if (loginErr) {
                console.error('[auth] session:', loginErr);
                return res.redirect(`${redirect}?login=error`);
            }
            res.redirect(`${redirect}?login=success`);
        });
    })(req, res, next);
});

router.get('/me', (req, res) => {
    if (!req.isAuthenticated()) {
        return res.status(401).json({ error: 'Not authenticated' });
    }
    res.json({ user: req.user.profile });
});

const userGuildCache = new Map();
const USER_GUILD_TTL_MS = 30 * 1000;

async function fetchUserGuilds(accessToken) {
    const cached = userGuildCache.get(accessToken);
    if (cached && Date.now() - cached.at < USER_GUILD_TTL_MS) return cached.guilds;

    let response = await fetch('https://discord.com/api/users/@me/guilds', {
        headers: { Authorization: `Bearer ${accessToken}` }
    });

    if (response.status === 429) {
        if (cached?.guilds) return cached.guilds;
        const retry = Number(response.headers.get('retry-after')) || 1;
        await new Promise((r) => setTimeout(r, Math.min(retry, 3) * 1000));
        response = await fetch('https://discord.com/api/users/@me/guilds', {
            headers: { Authorization: `Bearer ${accessToken}` }
        });
    }

    const guilds = await response.json();
    if (!response.ok || !Array.isArray(guilds)) {
        if (cached?.guilds) return cached.guilds;
        throw new Error(`user guilds ${response.status}`);
    }

    userGuildCache.set(accessToken, { guilds, at: Date.now() });
    return guilds;
}

router.get('/guilds', async (req, res) => {
    if (!req.isAuthenticated()) {
        return res.status(401).json({ error: 'Not authenticated' });
    }

    try {
        const [guilds, botGuildIds] = await Promise.all([
            fetchUserGuilds(req.user.accessToken),
            fetchBotGuildIds()
        ]);

        const adminGuilds = guilds
            .filter((g) => {
                try {
                    return (BigInt(g.permissions) & 8n) === 8n;
                } catch {
                    return false;
                }
            })
            .map((g) => ({
                id: String(g.id),
                name: g.name,
                icon: g.icon,
                banner: g.banner,
                hasBot: botGuildIds.has(String(g.id))
            }));

        res.json({ guilds: adminGuilds });
    } catch (err) {
        console.error('[auth] guilds:', err.message);
        res.status(502).json({ error: 'Failed to fetch guilds' });
    }
});

router.post('/logout', (req, res) => {
    req.logout(() => res.json({ ok: true }));
});

export default router;
