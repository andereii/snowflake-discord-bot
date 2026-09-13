import { EmbedBuilder, PermissionFlagsBits } from 'discord.js';
import axios from 'axios';
import { execFile } from 'child_process';
import util from 'util';
import db from './database.js';
import MessagesService from './messagesService.js';

const execFileAsync = util.promisify(execFile);

/**
 * Resolve a YouTube URL, handle or ID to { channelId, channelName }
 */
export async function resolveChannel(input) {
    if (!input || typeof input !== 'string') return null;
    let url = input.trim();

    if (url.startsWith('@')) {
        url = `https://www.youtube.com/${url}`;
    } else if (!url.startsWith('http://') && !url.startsWith('https://')) {
        url = `https://www.youtube.com/@${url}`;
    }

    try {
        const res = await axios.get(url, {
            headers: {
                'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
                'Accept-Language': 'en-US,en;q=0.9'
            },
            timeout: 10000
        });

        const idMatch = res.data.match(/"channelId":"(UC[a-zA-Z0-9_-]{22})"/) ||
                        res.data.match(/"browseId":"(UC[a-zA-Z0-9_-]{22})"/) ||
                        res.data.match(/channel\/(UC[a-zA-Z0-9_-]{22})/);

        const nameMatch = res.data.match(/<meta property="og:title" content="([^"]+)"/) ||
                          res.data.match(/"title":"([^"]+)"/);

        if (idMatch && idMatch[1]) {
            return {
                channelId: idMatch[1],
                channelName: nameMatch ? nameMatch[1] : idMatch[1]
            };
        }
    } catch (err) {
        // Fallback to yt-dlp
    }

    // Fallback using yt-dlp
    try {
        const { stdout } = await execFileAsync('yt-dlp', [
            '--no-playlist',
            '--no-warnings',
            '--print', 'channel_id',
            url
        ], { timeout: 15000 });

        const channelId = stdout.trim().split('\n').find(l => l.startsWith('UC'));
        if (!channelId) return null;

        // Try getting channel title
        let channelName = channelId;
        try {
            const { stdout: nameOut } = await execFileAsync('yt-dlp', [
                '--no-playlist',
                '--no-warnings',
                '--print', 'channel',
                `https://www.youtube.com/channel/${channelId}`
            ], { timeout: 10000 });
            const n = nameOut.trim().split('\n')[0];
            if (n) channelName = n;
        } catch {}

        return { channelId, channelName };
    } catch (err) {
        console.error('[youtubeService] Failed to resolve channel:', err.message);
        return null;
    }
}

/**
 * Fetch the latest video from a YouTube channel
 */
export async function getLatestVideo(channelId) {
    try {
        const { stdout } = await execFileAsync('yt-dlp', [
            '--no-warnings',
            '--flat-playlist',
            '--print', '%(id)s|%(title)s',
            '--playlist-end', '1',
            `https://www.youtube.com/channel/${channelId}`
        ], { timeout: 15000 });

        const line = stdout.trim().split('\n')[0];
        if (!line) return null;
        const [videoId, ...rest] = line.split('|');
        if (!videoId) return null;

        return {
            videoId: videoId.trim(),
            title: rest.join('|').trim(),
            link: `https://www.youtube.com/watch?v=${videoId.trim()}`
        };
    } catch (err) {
        console.error(`[youtubeService] Error fetching latest video for ${channelId}:`, err.message);
        return null;
    }
}

/**
 * Build the notification message text and embed
 */
export function buildNotification(sub, video, locale = 'en') {
    const template = sub.CustomMessage && sub.CustomMessage.trim()
        ? sub.CustomMessage
        : MessagesService.get(sub.GuildId, 'YouTube:NotiPorDefecto');

    const relativeTime = MessagesService.get(sub.GuildId, 'YouTube:HaceUnMomento');

    let text = template
        .replace(/{canal}/g, sub.YTChannelName || '')
        .replace(/{autor}/g, sub.YTChannelName || '')
        .replace(/{titulo}/g, video.title || '')
        .replace(/{url}/g, video.link)
        .replace(/{videoId}/g, video.videoId)
        .replace(/{subido}/g, new Date().toISOString())
        .replace(/{subidoREL}/g, relativeTime);

    if (sub.NotifyRoleId) {
        text = `<@&${sub.NotifyRoleId}> ${text}`;
    }

    if (!text.includes(video.link)) {
        text = `${text}\n${video.link}`;
    }

    if (text.length > 1900) {
        text = text.slice(0, 1899) + '…';
    }

    const embed = new EmbedBuilder()
        .setTitle(video.title || 'Nuevo vídeo')
        .setURL(video.link)
        .setDescription(`**${sub.YTChannelName || ''}**`)
        .setColor(0xFF0000)
        .setThumbnail(`https://i.ytimg.com/vi/${video.videoId}/hqdefault.jpg`)
        .setTimestamp();

    return { content: text, embeds: [embed] };
}

let notifierInterval = null;

/**
 * Check all YouTube subscriptions and notify new videos
 */
export async function checkSubscriptions(client) {
    try {
        const subs = db.prepare(`
            SELECT CAST(GuildId AS TEXT) as GuildId, YTChannelId, YTChannelName,
                   CAST(NotifyChannelId AS TEXT) as NotifyChannelId,
                   CAST(NotifyRoleId AS TEXT) as NotifyRoleId,
                   LastVideoId, CustomMessage
            FROM YouTubeSubscriptions
        `).all();

        if (!subs || subs.length === 0) return;

        // Group by channel ID to avoid duplicate HTTP requests
        const byChannel = new Map();
        for (const s of subs) {
            if (!byChannel.has(s.YTChannelId)) {
                byChannel.set(s.YTChannelId, []);
            }
            byChannel.get(s.YTChannelId).push(s);
        }

        for (const [channelId, channelSubs] of byChannel) {
            const latest = await getLatestVideo(channelId);
            if (!latest || !latest.videoId) continue;

            for (const sub of channelSubs) {
                // First time subscribing (backfill): mark latest video without notification
                if (!sub.LastVideoId) {
                    db.prepare('UPDATE YouTubeSubscriptions SET LastVideoId = ? WHERE GuildId = ?')
                        .run(latest.videoId, sub.GuildId);
                    continue;
                }

                // If new video detected
                if (latest.videoId !== sub.LastVideoId) {
                    db.prepare('UPDATE YouTubeSubscriptions SET LastVideoId = ? WHERE GuildId = ?')
                        .run(latest.videoId, sub.GuildId);

                    try {
                        const guild = client.guilds.cache.get(sub.GuildId);
                        if (!guild) continue;
                        const channel = guild.channels.cache.get(sub.NotifyChannelId);
                        if (!channel) continue;

                        const payload = buildNotification(sub, latest);
                        await channel.send(payload);
                    } catch (sendErr) {
                        console.error(`[youtubeService] Failed to notify guild ${sub.GuildId}:`, sendErr.message);
                    }
                }
            }
        }
    } catch (err) {
        console.error('[youtubeService] Error in checkSubscriptions:', err);
    }
}

/**
 * Start background notifier polling every 5 minutes
 */
export function startYouTubeNotifier(client, intervalMinutes = 5) {
    if (notifierInterval) clearInterval(notifierInterval);
    // Initial check after 30 seconds
    setTimeout(() => checkSubscriptions(client), 30000);
    notifierInterval = setInterval(() => checkSubscriptions(client), intervalMinutes * 60 * 1000);
    console.log(`[youtubeService] YouTube notifier service running (interval: ${intervalMinutes} min)`);
}

export function stopYouTubeNotifier() {
    if (notifierInterval) {
        clearInterval(notifierInterval);
        notifierInterval = null;
    }
}

export default {
    resolveChannel,
    getLatestVideo,
    buildNotification,
    checkSubscriptions,
    startYouTubeNotifier,
    stopYouTubeNotifier
};
