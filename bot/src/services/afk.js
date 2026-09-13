import db from './database.js';

db.exec(`
CREATE TABLE IF NOT EXISTS AfkUsers (
    GuildId INTEGER NOT NULL,
    UserId INTEGER NOT NULL,
    Reason TEXT NOT NULL,
    SetAt TEXT NOT NULL,
    OriginalNickname TEXT,
    PRIMARY KEY (GuildId, UserId)
);
CREATE TABLE IF NOT EXISTS AfkIgnoredChannels (
    GuildId INTEGER NOT NULL,
    ChannelId INTEGER NOT NULL,
    PRIMARY KEY (GuildId, ChannelId)
);
`);

const mentionCooldowns = new Map();

function parseSetAt(value) {
    if (value == null) return Date.now();
    if (typeof value === 'number' && Number.isFinite(value)) return value;
    const ms = Date.parse(String(value));
    return Number.isFinite(ms) ? ms : Date.now();
}

function rowToAfk(row) {
    if (!row) return null;
    return {
        guildId: String(row.GuildId),
        userId: String(row.UserId),
        reason: row.Reason || 'AFK',
        timestamp: parseSetAt(row.SetAt),
        originalNickname: row.OriginalNickname ?? null
    };
}

async function applyAfkNick(member) {
    try {
        if (!member?.manageable) return;
        const current = member.nickname || member.user.username;
        if (current.startsWith('[AFK] ')) return;
        await member.setNickname(`[AFK] ${current}`.slice(0, 32));
    } catch { /* hierarchy / permissions */ }
}

async function restoreNick(member, originalNickname) {
    try {
        if (!member?.manageable) return;
        const current = member.nickname || member.user.username;
        if (!current.startsWith('[AFK] ')) return;
        await member.setNickname(originalNickname || null);
    } catch { /* ignore */ }
}

export function getAfk(guildId, userId) {
    const row = db.prepare(`
        SELECT GuildId, UserId, Reason, SetAt, OriginalNickname
        FROM AfkUsers WHERE GuildId = ? AND UserId = ?
    `).get(guildId, userId);
    return rowToAfk(row);
}

export function listAfk(guildId) {
    return db.prepare(`
        SELECT GuildId, UserId, Reason, SetAt, OriginalNickname
        FROM AfkUsers WHERE GuildId = ? ORDER BY SetAt ASC
    `).all(guildId).map(rowToAfk);
}

export async function setAfk(member, reason) {
    const cleaned = (!reason || !String(reason).trim()) ? 'AFK' : String(reason).trim().slice(0, 250);
    const original = member.nickname || null;
    db.prepare(`
        INSERT INTO AfkUsers (GuildId, UserId, Reason, SetAt, OriginalNickname)
        VALUES (?, ?, ?, ?, ?)
        ON CONFLICT(GuildId, UserId) DO UPDATE SET
            Reason = excluded.Reason,
            SetAt = excluded.SetAt,
            OriginalNickname = excluded.OriginalNickname
    `).run(member.guild.id, member.id, cleaned, new Date().toISOString(), original);
    await applyAfkNick(member);
    return cleaned;
}

export async function removeAfk(member) {
    const existing = getAfk(member.guild.id, member.id);
    if (!existing) return false;
    db.prepare('DELETE FROM AfkUsers WHERE GuildId = ? AND UserId = ?').run(member.guild.id, member.id);
    await restoreNick(member, existing.originalNickname);
    return true;
}

export async function removeAllAfk(guild) {
    const rows = listAfk(guild.id);
    for (const row of rows) {
        const member = await guild.members.fetch(row.userId).catch(() => null);
        if (member) await restoreNick(member, row.originalNickname);
    }
    const result = db.prepare('DELETE FROM AfkUsers WHERE GuildId = ?').run(guild.id);
    return result.changes;
}

export function resetAfkReason(guildId, userId) {
    const existing = getAfk(guildId, userId);
    if (!existing) return false;
    db.prepare('UPDATE AfkUsers SET Reason = ? WHERE GuildId = ? AND UserId = ?').run('AFK', guildId, userId);
    return true;
}

export function addIgnoredChannel(guildId, channelId) {
    const result = db.prepare(`
        INSERT OR IGNORE INTO AfkIgnoredChannels (GuildId, ChannelId) VALUES (?, ?)
    `).run(guildId, channelId);
    return result.changes > 0;
}

export function removeIgnoredChannel(guildId, channelId) {
    const result = db.prepare(
        'DELETE FROM AfkIgnoredChannels WHERE GuildId = ? AND ChannelId = ?'
    ).run(guildId, channelId);
    return result.changes > 0;
}

export function listIgnoredChannels(guildId) {
    return db.prepare(
        'SELECT CAST(ChannelId AS TEXT) AS ChannelId FROM AfkIgnoredChannels WHERE GuildId = ?'
    ).all(guildId).map(row => row.ChannelId);
}

export function isIgnoredChannel(guildId, channelId) {
    const row = db.prepare(
        'SELECT 1 FROM AfkIgnoredChannels WHERE GuildId = ? AND ChannelId = ?'
    ).get(guildId, channelId);
    return Boolean(row);
}

export function isOnCooldown(guildId, channelId, userId) {
    const key = `${guildId}-${channelId}-${userId}`;
    const expiration = mentionCooldowns.get(key);
    if (expiration && expiration > Date.now()) return true;
    mentionCooldowns.set(key, Date.now() + 8000);
    return false;
}

export default {
    getAfk,
    listAfk,
    setAfk,
    removeAfk,
    removeAllAfk,
    resetAfkReason,
    addIgnoredChannel,
    removeIgnoredChannel,
    listIgnoredChannels,
    isIgnoredChannel,
    isOnCooldown
};
