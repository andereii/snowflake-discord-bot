const MULTIPLIERS = { s: 1000, m: 60_000, h: 3_600_000, d: 86_400_000 };
export const MAX_TIMEOUT_MS = 28 * 86_400_000;
export const MAX_TEMP_ROLE_MS = 365 * 86_400_000;

export function parseDuration(str) {
    const match = str?.match(/^(\d+)(s|m|h|d)$/i);
    if (!match) return null;
    return parseInt(match[1], 10) * MULTIPLIERS[match[2].toLowerCase()];
}

export function formatCompactDuration(ms) {
    const totalSec = Math.max(0, Math.round(Number(ms) / 1000));
    const days = Math.floor(totalSec / 86400);
    const hours = Math.floor((totalSec % 86400) / 3600);
    const minutes = Math.floor((totalSec % 3600) / 60);
    const seconds = totalSec % 60;
    const parts = [];
    if (days) parts.push(`${days}d`);
    if (hours) parts.push(`${hours}h`);
    if (minutes) parts.push(`${minutes}m`);
    if (seconds && !days && !hours) parts.push(`${seconds}s`);
    return parts.join(' ') || '0s';
}
