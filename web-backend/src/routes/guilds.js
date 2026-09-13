import { Router } from 'express';
import {
    fetchGuildStats,
    fetchGuildMembers,
    fetchGuildRoles,
    fetchGuildEmojis,
    fetchGuildChannels
} from '../services/botProxy.js';

const router = Router();

router.get('/:guildId/stats', async (req, res) => {
    const stats = await fetchGuildStats(req.params.guildId);
    if (!stats) return res.status(404).json({ error: 'Guild not found' });
    res.json(stats);
});

router.get('/:guildId/members', async (req, res) => {
    res.json({ members: await fetchGuildMembers(req.params.guildId) });
});

router.get('/:guildId/roles', async (req, res) => {
    res.json({ roles: await fetchGuildRoles(req.params.guildId) });
});

router.get('/:guildId/emojis', async (req, res) => {
    res.json({ emojis: await fetchGuildEmojis(req.params.guildId) });
});

router.get('/:guildId/channels', async (req, res) => {
    res.json({ channels: await fetchGuildChannels(req.params.guildId) });
});

export default router;
