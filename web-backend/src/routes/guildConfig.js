import { Router } from 'express';
import db from '../db/index.js';
import { apiKeyGuard } from '../middleware/auth.js';

const router = Router();

// GET /api/guilds/:guildId/config — snapshot completo
router.get('/:guildId/config', (req, res) => {
  const { guildId } = req.params;
  const guildIdNum = String(guildId);

  const cfg = db.prepare('SELECT * FROM GuildConfigs WHERE GuildId = ?').get(guildIdNum);
  const counting = db.prepare('SELECT * FROM CountingConfigs WHERE GuildId = ?').get(guildIdNum);
  const youtube = db.prepare('SELECT * FROM YouTubeSubscriptions WHERE GuildId = ?').get(guildIdNum);
  const birthday = db.prepare('SELECT * FROM BirthdayConfigs WHERE GuildId = ?').get(guildIdNum);
  const blocked = db.prepare('SELECT ChannelId FROM ChannelLocks WHERE GuildId = ?').all(guildIdNum);

  // Valores por defecto si la fila no existe.
  const config = cfg || {
    GuildId: guildIdNum,
    Language: 'en',
    AiChatEnabled: 1,
    DownloadsEnabled: 1,
    AiWebSearchEnabled: 1,
    AiCommandsEnabled: 1
  };

  res.json({
    guildId: config.GuildId,
    language: config.Language || 'en',
    moderation: {
      logChannelId: config.ModLogChannelId ? String(config.ModLogChannelId) : null
    },
    welcome: {
      enabled: !!config.WelcomeChannelId,
      channelId: config.WelcomeChannelId ? String(config.WelcomeChannelId) : null,
      message: config.WelcomeMessage || null
    },
    voice: {
      hubChannelId: config.HubChannelId ? String(config.HubChannelId) : null,
      tempChannelNameTemplate: config.TempChannelNameTemplate || null
    },
    music: {
      volume: config.Volume ?? null,
      djRoleId: config.DjRoleId ? String(config.DjRoleId) : null
    },
    ai: {
      chatEnabled: !!config.AiChatEnabled,
      mentionsEnabled: !!config.AiMentionsEnabled,
      spontaneousEnabled: !!config.AiSpontaneousEnabled,
      webSearchEnabled: !!config.AiWebSearchEnabled,
      commandsEnabled: !!config.AiCommandsEnabled
    },
    downloads: {
      enabled: !!config.DownloadsEnabled
    },
    birthday: birthday ? {
      enabled: !!birthday.Enabled,
      channelId: birthday.ChannelId ? String(birthday.ChannelId) : null,
      hourUtc: birthday.HourUtc ?? 12,
      message: birthday.Message || ''
    } : null,
    counting: counting ? {
      channelId: counting.ChannelId ? String(counting.ChannelId) : null,
      base: counting.Base || 'Decimal',
      goal: counting.Goal ?? null,
      extraChancesPerDay: counting.ExtraChancesPerDay ?? 0,
      emojiCorrect: counting.EmojiCorrect || null,
      emojiIncorrect: counting.EmojiIncorrect || null,
      emojiRecord: counting.EmojiRecord || null,
      loseMessage: counting.LoseMessage || null
    } : null,
    youtube: youtube ? {
      channelId: youtube.YTChannelId || null,
      channelName: youtube.YTChannelName || null,
      notifyChannelId: youtube.NotifyChannelId ? String(youtube.NotifyChannelId) : null,
      notifyRoleId: youtube.NotifyRoleId ? String(youtube.NotifyRoleId) : null,
      customMessage: youtube.CustomMessage || null
    } : null,
    blockedChannels: blocked.map(b => String(b.ChannelId)),
    pollCount: cfg?.PollCount ?? 0
  });
});

// POST /api/guilds/:guildId/config — patch general
router.post('/:guildId/config', apiKeyGuard, (req, res) => {
  const { guildId } = req.params;
  const guildIdNum = String(guildId);
  const p = req.body;

  // Asegurar que la fila existe.
  const existe = db.prepare('SELECT GuildId FROM GuildConfigs WHERE GuildId = ?').get(guildIdNum);
  if (!existe) {
    db.prepare('INSERT INTO GuildConfigs (GuildId) VALUES (?)').run(guildIdNum);
  }

  const updates = [];
  const values = [];

  const set = (col, val) => {
    if (val !== undefined) {
      updates.push(`${col} = ?`);
      values.push(val);
    }
  };

  if (p.modLogChannelId !== undefined) set('ModLogChannelId', p.modLogChannelId ? Number(p.modLogChannelId) : null);
  if (p.welcomeChannelId !== undefined) set('WelcomeChannelId', p.welcomeChannelId ? Number(p.welcomeChannelId) : null);
  if (p.welcomeMessage !== undefined) set('WelcomeMessage', p.welcomeMessage || null);
  if (p.hubChannelId !== undefined) set('HubChannelId', p.hubChannelId ? Number(p.hubChannelId) : null);
  if (p.tempChannelNameTemplate !== undefined) set('TempChannelNameTemplate', p.tempChannelNameTemplate || null);
  if (p.volume !== undefined) set('Volume', p.volume !== null ? Math.max(0, Math.min(100, p.volume)) : null);
  if (p.djRoleId !== undefined) set('DjRoleId', p.djRoleId ? Number(p.djRoleId) : null);
  if (p.aiChatEnabled !== undefined) set('AiChatEnabled', p.aiChatEnabled ? 1 : 0);
  if (p.aiMentionsEnabled !== undefined) set('AiMentionsEnabled', p.aiMentionsEnabled ? 1 : 0);
  if (p.aiSpontaneousEnabled !== undefined) set('AiSpontaneousEnabled', p.aiSpontaneousEnabled ? 1 : 0);
  if (p.aiWebSearchEnabled !== undefined) set('AiWebSearchEnabled', p.aiWebSearchEnabled ? 1 : 0);
  if (p.aiCommandsEnabled !== undefined) set('AiCommandsEnabled', p.aiCommandsEnabled ? 1 : 0);
  if (p.downloadsEnabled !== undefined) set('DownloadsEnabled', p.downloadsEnabled ? 1 : 0);
  if (p.language !== undefined) set('Language', p.language || 'en');

  if (updates.length > 0) {
    values.push(guildIdNum);
    db.prepare(`UPDATE GuildConfigs SET ${updates.join(', ')} WHERE GuildId = ?`).run(...values);
  }

  res.json({ ok: true });
});

export default router;
