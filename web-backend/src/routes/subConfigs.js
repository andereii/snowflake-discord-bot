import { Router } from 'express';
import db from '../db/index.js';
import { apiKeyGuard } from '../middleware/auth.js';
import { ensureRow, applyPatch } from '../lib/sqlPatch.js';

const router = Router();
const num = (v) => (v ? Number(v) : null);

router.post('/:guildId/config/counting', apiKeyGuard, (req, res) => {
    const guildId = String(req.params.guildId);
    const p = req.body;
    ensureRow(db, 'CountingConfigs', 'GuildId', guildId);
    applyPatch(db, 'CountingConfigs', 'GuildId', guildId, [
        ['ChannelId', p.channelId !== undefined ? num(p.channelId) : undefined],
        ['Base', p.base !== undefined ? (p.base || 'Decimal') : undefined],
        ['Goal', p.goal !== undefined ? (p.goal ?? null) : undefined],
        ['ExtraChancesPerDay', p.extraChancesPerDay !== undefined ? Math.max(0, Math.min(10, p.extraChancesPerDay)) : undefined],
        ['EmojiCorrect', p.emojiCorrect !== undefined ? (p.emojiCorrect || null) : undefined],
        ['EmojiIncorrect', p.emojiIncorrect !== undefined ? (p.emojiIncorrect || null) : undefined],
        ['EmojiRecord', p.emojiRecord !== undefined ? (p.emojiRecord || null) : undefined],
        ['LoseMessage', p.loseMessage !== undefined ? (p.loseMessage || null) : undefined]
    ]);
    res.json({ ok: true });
});

router.post('/:guildId/config/youtube', apiKeyGuard, (req, res) => {
    const guildId = String(req.params.guildId);
    const p = req.body;
    ensureRow(db, 'YouTubeSubscriptions', 'GuildId', guildId);
    applyPatch(db, 'YouTubeSubscriptions', 'GuildId', guildId, [
        ['YTChannelId', p.ytChannelId !== undefined ? (p.ytChannelId || null) : undefined],
        ['YTChannelName', p.ytChannelName !== undefined ? (p.ytChannelName || null) : undefined],
        ['NotifyChannelId', p.notifyChannelId !== undefined ? num(p.notifyChannelId) : undefined],
        ['NotifyRoleId', p.notifyRoleId !== undefined ? num(p.notifyRoleId) : undefined],
        ['CustomMessage', p.customMessage !== undefined ? (p.customMessage || null) : undefined]
    ]);
    res.json({ ok: true });
});

router.delete('/:guildId/config/youtube', apiKeyGuard, (req, res) => {
    const result = db.prepare('DELETE FROM YouTubeSubscriptions WHERE GuildId = ?').run(String(req.params.guildId));
    if (result.changes === 0) return res.status(404).json({ error: 'No subscription' });
    res.status(204).send();
});

router.get('/:guildId/config/birthday', (req, res) => {
    const row = db.prepare('SELECT * FROM BirthdayConfigs WHERE GuildId = ?').get(String(req.params.guildId));
    const cfg = row || {
        Enabled: 0,
        ChannelId: null,
        HourUtc: 12,
        Message: '¡Feliz cumpleaños {usuario}! 🎂🎉'
    };
    res.json({
        enabled: !!cfg.Enabled,
        channelId: cfg.ChannelId ? String(cfg.ChannelId) : null,
        hourUtc: cfg.HourUtc ?? 12,
        message: cfg.Message
    });
});

router.post('/:guildId/config/birthday', apiKeyGuard, (req, res) => {
    const guildId = String(req.params.guildId);
    const p = req.body;
    ensureRow(db, 'BirthdayConfigs', 'GuildId', guildId);
    applyPatch(db, 'BirthdayConfigs', 'GuildId', guildId, [
        ['Enabled', p.enabled !== undefined ? (p.enabled ? 1 : 0) : undefined],
        ['ChannelId', p.channelId !== undefined ? num(p.channelId) : undefined],
        ['HourUtc', p.hourUtc !== undefined ? Math.max(0, Math.min(23, p.hourUtc)) : undefined],
        ['Message', p.message !== undefined ? (p.message || '') : undefined]
    ]);
    res.json({ ok: true });
});

export default router;
