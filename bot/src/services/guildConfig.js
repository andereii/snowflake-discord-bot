import db from './database.js';

const COLUMNS = new Set([
    'Language',
    'ModLogChannelId',
    'WelcomeChannelId',
    'WelcomeMessage',
    'HubChannelId',
    'TempChannelNameTemplate',
    'Volume',
    'DjRoleId',
    'AiChatEnabled',
    'AiMentionsEnabled',
    'AiSpontaneousEnabled',
    'AiWebSearchEnabled',
    'AiCommandsEnabled',
    'DownloadsEnabled',
    'PollCount'
]);

export function ensureGuild(guildId) {
    db.prepare('INSERT OR IGNORE INTO GuildConfigs (GuildId) VALUES (?)').run(String(guildId));
}

export function getGuildConfig(guildId) {
    ensureGuild(guildId);
    return db.prepare('SELECT * FROM GuildConfigs WHERE GuildId = ?').get(String(guildId));
}

export function updateGuildConfig(guildId, patch) {
    ensureGuild(guildId);
    const entries = Object.entries(patch).filter(([key]) => COLUMNS.has(key));
    if (!entries.length) return getGuildConfig(guildId);
    const sets = entries.map(([key]) => `${key} = ?`).join(', ');
    db.prepare(`UPDATE GuildConfigs SET ${sets} WHERE GuildId = ?`)
        .run(...entries.map(([, value]) => value), String(guildId));
    return getGuildConfig(guildId);
}

export function isEnabled(row, column, defaultEnabled = true) {
    if (!row || row[column] == null) return defaultEnabled;
    return row[column] !== 0;
}
