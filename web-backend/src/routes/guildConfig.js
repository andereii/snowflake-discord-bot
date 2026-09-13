import { Router } from 'express';
import db from '../db/index.js';
import { apiKeyGuard } from '../middleware/auth.js';
import { ensureRow, applyPatch } from '../lib/sqlPatch.js';

const router = Router();

function asId(value) {
    return value ? String(value) : null;
}

router.get('/:guildId/config', (req, res) => {
    const guildId = String(req.params.guildId);
    const cfg = db.prepare('SELECT * FROM GuildConfigs WHERE GuildId = ?').get(guildId) || {
        GuildId: guildId,
        Language: 'en',
        AiChatEnabled: 1,
        DownloadsEnabled: 1,
        AiWebSearchEnabled: 1,
        AiCommandsEnabled: 1
    };
    const counting = db.prepare('SELECT * FROM CountingConfigs WHERE GuildId = ?').get(guildId);
    const youtube = db.prepare('SELECT * FROM YouTubeSubscriptions WHERE GuildId = ?').get(guildId);
    const birthday = db.prepare('SELECT * FROM BirthdayConfigs WHERE GuildId = ?').get(guildId);
    const blocked = db.prepare('SELECT ChannelId FROM ChannelLocks WHERE GuildId = ?').all(guildId);

    res.json({
        guildId: cfg.GuildId,
        language: cfg.Language || 'en',
        moderation: { logChannelId: asId(cfg.ModLogChannelId) },
        welcome: {
            enabled: !!cfg.WelcomeChannelId,
            channelId: asId(cfg.WelcomeChannelId),
            message: cfg.WelcomeMessage || null
        },
        voice: {
            hubChannelId: asId(cfg.HubChannelId),
            tempChannelNameTemplate: cfg.TempChannelNameTemplate || null
        },
        music: {
            volume: cfg.Volume ?? null,
            djRoleId: asId(cfg.DjRoleId)
        },
        ai: {
            chatEnabled: !!cfg.AiChatEnabled,
            mentionsEnabled: !!cfg.AiMentionsEnabled,
            spontaneousEnabled: !!cfg.AiSpontaneousEnabled,
            webSearchEnabled: !!cfg.AiWebSearchEnabled,
            commandsEnabled: !!cfg.AiCommandsEnabled
        },
        downloads: { enabled: !!cfg.DownloadsEnabled },
        birthday: birthday ? {
            enabled: !!birthday.Enabled,
            channelId: asId(birthday.ChannelId),
            hourUtc: birthday.HourUtc ?? 12,
            message: birthday.Message || ''
        } : null,
        counting: counting ? {
            channelId: asId(counting.ChannelId),
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
            notifyChannelId: asId(youtube.NotifyChannelId),
            notifyRoleId: asId(youtube.NotifyRoleId),
            customMessage: youtube.CustomMessage || null
        } : null,
        blockedChannels: blocked.map(b => String(b.ChannelId)),
        pollCount: cfg.PollCount ?? 0
    });
});

router.post('/:guildId/config', apiKeyGuard, (req, res) => {
    const guildId = String(req.params.guildId);
    const p = req.body;
    ensureRow(db, 'GuildConfigs', 'GuildId', guildId);

    const num = (v) => (v ? Number(v) : null);
    applyPatch(db, 'GuildConfigs', 'GuildId', guildId, [
        ['ModLogChannelId', p.modLogChannelId !== undefined ? num(p.modLogChannelId) : undefined],
        ['WelcomeChannelId', p.welcomeChannelId !== undefined ? num(p.welcomeChannelId) : undefined],
        ['WelcomeMessage', p.welcomeMessage !== undefined ? (p.welcomeMessage || null) : undefined],
        ['HubChannelId', p.hubChannelId !== undefined ? num(p.hubChannelId) : undefined],
        ['TempChannelNameTemplate', p.tempChannelNameTemplate !== undefined ? (p.tempChannelNameTemplate || null) : undefined],
        ['Volume', p.volume !== undefined ? (p.volume !== null ? Math.max(0, Math.min(100, p.volume)) : null) : undefined],
        ['DjRoleId', p.djRoleId !== undefined ? num(p.djRoleId) : undefined],
        ['AiChatEnabled', p.aiChatEnabled !== undefined ? (p.aiChatEnabled ? 1 : 0) : undefined],
        ['AiMentionsEnabled', p.aiMentionsEnabled !== undefined ? (p.aiMentionsEnabled ? 1 : 0) : undefined],
        ['AiSpontaneousEnabled', p.aiSpontaneousEnabled !== undefined ? (p.aiSpontaneousEnabled ? 1 : 0) : undefined],
        ['AiWebSearchEnabled', p.aiWebSearchEnabled !== undefined ? (p.aiWebSearchEnabled ? 1 : 0) : undefined],
        ['AiCommandsEnabled', p.aiCommandsEnabled !== undefined ? (p.aiCommandsEnabled ? 1 : 0) : undefined],
        ['DownloadsEnabled', p.downloadsEnabled !== undefined ? (p.downloadsEnabled ? 1 : 0) : undefined],
        ['Language', p.language !== undefined ? (p.language || 'en') : undefined]
    ]);

    res.json({ ok: true });
});

export default router;
