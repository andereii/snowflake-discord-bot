import db from './database.js';

db.exec(`
CREATE TABLE IF NOT EXISTS TimedRoles (
    GuildId INTEGER NOT NULL,
    UserId INTEGER NOT NULL,
    RoleId INTEGER NOT NULL,
    ExpiresAt TEXT NOT NULL,
    PRIMARY KEY (GuildId, UserId, RoleId)
);
`);

const timers = new Map();
const MAX_TIMEOUT = 2_147_483_647;
let clientRef = null;

function keyOf(guildId, userId, roleId) {
    return `${guildId}:${userId}:${roleId}`;
}

function clearTimer(key) {
    const timer = timers.get(key);
    if (timer) {
        clearTimeout(timer);
        timers.delete(key);
    }
}

async function expireRole(guildId, userId, roleId) {
    db.prepare('DELETE FROM TimedRoles WHERE GuildId = ? AND UserId = ? AND RoleId = ?')
        .run(guildId, userId, roleId);
    const guild = clientRef?.guilds.cache.get(String(guildId));
    if (!guild) return;
    const member = await guild.members.fetch(String(userId)).catch(() => null);
    const role = guild.roles.cache.get(String(roleId));
    if (!member || !role || !member.roles.cache.has(role.id)) return;
    await member.roles.remove(role, 'Temporary role expired').catch(() => {});
}

function armTimer(guildId, userId, roleId, expiresAtIso) {
    const key = keyOf(guildId, userId, roleId);
    clearTimer(key);
    const delay = new Date(expiresAtIso).getTime() - Date.now();
    if (delay <= 0) {
        void expireRole(guildId, userId, roleId);
        return;
    }
    const wait = Math.min(delay, MAX_TIMEOUT);
    const timer = setTimeout(() => {
        timers.delete(key);
        const remaining = new Date(expiresAtIso).getTime() - Date.now();
        if (remaining <= 0) void expireRole(guildId, userId, roleId);
        else armTimer(guildId, userId, roleId, expiresAtIso);
    }, wait);
    timers.set(key, timer);
}

export function scheduleTimedRole(guildId, userId, roleId, durationMs) {
    const expiresAt = new Date(Date.now() + durationMs).toISOString();
    db.prepare(`
        INSERT INTO TimedRoles (GuildId, UserId, RoleId, ExpiresAt)
        VALUES (?, ?, ?, ?)
        ON CONFLICT(GuildId, UserId, RoleId) DO UPDATE SET ExpiresAt = excluded.ExpiresAt
    `).run(guildId, userId, roleId, expiresAt);
    armTimer(guildId, userId, roleId, expiresAt);
    return expiresAt;
}

export function cancelTimedRole(guildId, userId, roleId) {
    clearTimer(keyOf(guildId, userId, roleId));
    db.prepare('DELETE FROM TimedRoles WHERE GuildId = ? AND UserId = ? AND RoleId = ?')
        .run(guildId, userId, roleId);
}

export function initTimedRoleScheduler(client) {
    clientRef = client;
    const rows = db.prepare('SELECT GuildId, UserId, RoleId, ExpiresAt FROM TimedRoles').all();
    for (const row of rows) {
        armTimer(String(row.GuildId), String(row.UserId), String(row.RoleId), row.ExpiresAt);
    }
}
