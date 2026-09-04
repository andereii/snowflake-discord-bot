import { Router } from 'express';
import db from '../db/index.js';
import { apiKeyGuard } from '../middleware/auth.js';

const router = Router();

// POST /api/guilds/:guildId/config/counting
router.post('/:guildId/config/counting', apiKeyGuard, (req, res) => {
  const { guildId } = req.params;
  const guildIdNum = String(guildId);
  const p = req.body;

  const existe = db.prepare('SELECT GuildId FROM CountingConfigs WHERE GuildId = ?').get(guildIdNum);
  if (!existe) {
    db.prepare('INSERT INTO CountingConfigs (GuildId) VALUES (?)').run(guildIdNum);
  }

  const updates = [];
  const values = [];
  const set = (col, val) => {
    if (val !== undefined) {
      updates.push(`${col} = ?`);
      values.push(val);
    }
  };

  if (p.channelId !== undefined) set('ChannelId', p.channelId ? Number(p.channelId) : null);
  if (p.base !== undefined) set('Base', p.base || 'Decimal');
  if (p.goal !== undefined) set('Goal', p.goal ?? null);
  if (p.extraChancesPerDay !== undefined) set('ExtraChancesPerDay', Math.max(0, Math.min(10, p.extraChancesPerDay)));
  if (p.emojiCorrect !== undefined) set('EmojiCorrect', p.emojiCorrect || null);
  if (p.emojiIncorrect !== undefined) set('EmojiIncorrect', p.emojiIncorrect || null);
  if (p.emojiRecord !== undefined) set('EmojiRecord', p.emojiRecord || null);
  if (p.loseMessage !== undefined) set('LoseMessage', p.loseMessage || null);

  if (updates.length > 0) {
    values.push(guildIdNum);
    db.prepare(`UPDATE CountingConfigs SET ${updates.join(', ')} WHERE GuildId = ?`).run(...values);
  }

  res.json({ ok: true });
});

// POST /api/guilds/:guildId/config/youtube
router.post('/:guildId/config/youtube', apiKeyGuard, (req, res) => {
  const { guildId } = req.params;
  const guildIdNum = String(guildId);
  const p = req.body;

  const existe = db.prepare('SELECT GuildId FROM YouTubeSubscriptions WHERE GuildId = ?').get(guildIdNum);
  if (!existe) {
    db.prepare('INSERT INTO YouTubeSubscriptions (GuildId) VALUES (?)').run(guildIdNum);
  }

  const updates = [];
  const values = [];
  const set = (col, val) => {
    if (val !== undefined) {
      updates.push(`${col} = ?`);
      values.push(val);
    }
  };

  if (p.ytChannelId !== undefined) set('YTChannelId', p.ytChannelId || null);
  if (p.ytChannelName !== undefined) set('YTChannelName', p.ytChannelName || null);
  if (p.notifyChannelId !== undefined) set('NotifyChannelId', p.notifyChannelId ? Number(p.notifyChannelId) : null);
  if (p.notifyRoleId !== undefined) set('NotifyRoleId', p.notifyRoleId ? Number(p.notifyRoleId) : null);
  if (p.customMessage !== undefined) set('CustomMessage', p.customMessage || null);

  if (updates.length > 0) {
    values.push(guildIdNum);
    db.prepare(`UPDATE YouTubeSubscriptions SET ${updates.join(', ')} WHERE GuildId = ?`).run(...values);
  }

  res.json({ ok: true });
});

// DELETE /api/guilds/:guildId/config/youtube
router.delete('/:guildId/config/youtube', apiKeyGuard, (req, res) => {
  const { guildId } = req.params;
  const guildIdNum = String(guildId);
  const result = db.prepare('DELETE FROM YouTubeSubscriptions WHERE GuildId = ?').run(guildIdNum);
  if (result.changes === 0) return res.status(404).json({ error: 'No existe suscripción' });
  res.status(204).send();
});

// GET /api/guilds/:guildId/config/birthday
router.get('/:guildId/config/birthday', (req, res) => {
  const { guildId } = req.params;
  const row = db.prepare('SELECT * FROM BirthdayConfigs WHERE GuildId = ?').get(String(guildId));
  const cfg = row || { GuildId: String(guildId), Enabled: 0, ChannelId: null, HourUtc: 12, Message: '¡Feliz cumpleaños {usuario}! 🎂🎉' };
  res.json({
    enabled: !!cfg.Enabled,
    channelId: cfg.ChannelId ? String(cfg.ChannelId) : null,
    hourUtc: cfg.HourUtc ?? 12,
    message: cfg.Message
  });
});

// POST /api/guilds/:guildId/config/birthday
router.post('/:guildId/config/birthday', apiKeyGuard, (req, res) => {
  const { guildId } = req.params;
  const guildIdNum = String(guildId);
  const p = req.body;

  const existe = db.prepare('SELECT GuildId FROM BirthdayConfigs WHERE GuildId = ?').get(guildIdNum);
  if (!existe) {
    db.prepare('INSERT INTO BirthdayConfigs (GuildId) VALUES (?)').run(guildIdNum);
  }

  const updates = [];
  const values = [];
  const set = (col, val) => {
    if (val !== undefined) {
      updates.push(`${col} = ?`);
      values.push(val);
    }
  };

  if (p.enabled !== undefined) set('Enabled', p.enabled ? 1 : 0);
  if (p.channelId !== undefined) set('ChannelId', p.channelId ? Number(p.channelId) : null);
  if (p.hourUtc !== undefined) set('HourUtc', Math.max(0, Math.min(23, p.hourUtc)));
  if (p.message !== undefined) set('Message', p.message || '');

  if (updates.length > 0) {
    values.push(guildIdNum);
    db.prepare(`UPDATE BirthdayConfigs SET ${updates.join(', ')} WHERE GuildId = ?`).run(...values);
  }

  res.json({ ok: true });
});

export default router;
