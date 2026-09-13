import { spawn, execFile, execFileSync } from 'child_process';
import { promisify } from 'util';
import fs from 'fs';
import path from 'path';
import ffmpegStatic from 'ffmpeg-static';
import {
    joinVoiceChannel,
    createAudioPlayer,
    createAudioResource,
    AudioPlayerStatus,
    VoiceConnectionStatus,
    entersState,
    getVoiceConnection,
    StreamType,
    NoSubscriberBehavior
} from '@discordjs/voice';
import { EmbedBuilder, PermissionFlagsBits } from 'discord.js';
import { getGuildConfig, updateGuildConfig } from './guildConfig.js';
import MessagesService from './messagesService.js';
import { isLocalRuntime } from '../lib/runtime.js';
import { parseDuration as parseShortDuration } from '../lib/duration.js';

const execFileAsync = promisify(execFile);
const queues = new Map();
const PLAYLIST_CAP = 50;
const META_TIMEOUT_MS = 25_000;
const PLAYLIST_TIMEOUT_MS = 45_000;
const AUDIO_EXT = /\.(mp3|wav|flac|ogg|oga|m4a|aac|webm|opus)(\?|$)/i;
const LOOP_CYCLE = ['off', 'track', 'queue'];

function resolveFfmpegBin() {
    // ffmpeg-static (johnvansickle 7.0.2) SIGSEGVs on YouTube webm + -ss/-t,
    // which yt-dlp uses for --download-sections (seek / bass / nightcore restart).
    const candidates = ['/usr/bin/ffmpeg', ffmpegStatic].filter(Boolean);
    for (const candidate of candidates) {
        if (fs.existsSync(candidate)) return candidate;
    }
    return 'ffmpeg';
}

const ffmpegBin = resolveFfmpegBin();

const ui = {
    onTrackChange: async () => {},
    onStopped: async () => {}
};

export function setMusicUi(handlers) {
    Object.assign(ui, handlers);
}

wireFfmpegPath();
assertYtDlp();
console.log(`[music] ffmpeg: ${ffmpegBin}`);

function wireFfmpegPath() {
    process.env.FFMPEG_PATH = ffmpegBin;
    const dir = path.dirname(ffmpegBin);
    if (!dir || dir === '.') return;
    const parts = (process.env.PATH || '').split(path.delimiter);
    if (!parts.includes(dir)) {
        process.env.PATH = `${dir}${path.delimiter}${process.env.PATH || ''}`;
    }
}

function assertYtDlp() {
    try {
        execFileSync('yt-dlp', ['--version'], { timeout: 5000, stdio: 'ignore' });
    } catch {
        console.error('[music] yt-dlp is not on PATH; /play will not work');
    }
}

function cookieArgs() {
    const file = process.env.YT_COOKIES_FILE;
    if (file && fs.existsSync(file)) return ['--cookies', file];
    return [];
}

function ytDlpFfmpegArgs() {
    return ['--ffmpeg-location', ffmpegBin];
}

function drain(stream) {
    if (!stream) return;
    stream.on('data', () => {});
    stream.on('error', () => {});
}

function clampVolume(percent) {
    const n = Number(percent);
    if (!Number.isFinite(n)) return 100;
    return Math.max(0, Math.min(100, Math.round(n)));
}

function savedVolumePercent(guildId) {
    const cfg = getGuildConfig(guildId);
    return cfg?.Volume == null ? 100 : clampVolume(cfg.Volume);
}

function guildIdOf(ctx) {
    return ctx.guildId || ctx.guild?.id;
}

function isHttpUrl(text) {
    try {
        const url = new URL(text);
        return url.protocol === 'http:' || url.protocol === 'https:';
    } catch {
        return false;
    }
}

function isDirectAudioUrl(url) {
    try {
        return AUDIO_EXT.test(new URL(url).pathname);
    } catch {
        return false;
    }
}

function isPlaylistUrl(text) {
    try {
        const url = new URL(text);
        const pathName = url.pathname.toLowerCase();
        if (pathName.includes('/playlist')) return true;
        if (pathName.includes('/sets/')) return true;
        if (pathName.includes('/album/')) return true;
        return false;
    } catch {
        return false;
    }
}

function parseSpotifyTrackUrl(text) {
    const trimmed = text.trim();
    if (trimmed.toLowerCase().startsWith('spotify:track:')) {
        const id = trimmed.slice('spotify:track:'.length).trim();
        return id ? `https://open.spotify.com/track/${id}` : null;
    }
    try {
        const url = new URL(trimmed);
        const host = url.hostname.toLowerCase();
        if (!['open.spotify.com', 'play.spotify.com', 'embed.spotify.com'].includes(host)) return null;
        const parts = url.pathname.split('/').filter(Boolean);
        const index = parts.findIndex(part => part.toLowerCase() === 'track');
        if (index < 0 || !parts[index + 1]) return null;
        return `https://open.spotify.com/track/${parts[index + 1]}`;
    } catch {
        return null;
    }
}

function parseTrack(line) {
    let data;
    try {
        data = JSON.parse(line);
    } catch {
        return null;
    }

    const extractor = String(data.extractor || data.ie_key || '').toLowerCase();
    let url = data.webpage_url || data.original_url || data.url || null;
    if (url && !/^https?:\/\//i.test(url)) {
        url = extractor.includes('youtube') && data.id
            ? `https://www.youtube.com/watch?v=${data.id}`
            : null;
    }
    if (!url && extractor.includes('youtube') && data.id) {
        url = `https://www.youtube.com/watch?v=${data.id}`;
    }
    if (!url) return null;

    let thumbnail = data.thumbnail || null;
    if (!thumbnail && Array.isArray(data.thumbnails) && data.thumbnails.length) {
        thumbnail = data.thumbnails[data.thumbnails.length - 1]?.url || null;
    }
    if (!thumbnail && extractor.includes('youtube') && data.id) {
        thumbnail = `https://i.ytimg.com/vi/${data.id}/hqdefault.jpg`;
    }

    const duration = Number.isFinite(data.duration) ? data.duration : null;
    return {
        title: data.title || data.id || 'Unknown',
        url,
        duration,
        thumbnail,
        author: data.artist || data.uploader || data.channel || data.creator || 'Unknown',
        isLive: Boolean(data.is_live),
        isAttachment: false,
        playlistTitle: data.playlist_title || data.playlist || null
    };
}

async function dumpTracks(query, { playlist = false } = {}) {
    const args = [
        '--dump-json',
        '--skip-download',
        '--no-warnings',
        '--no-progress',
        ...ytDlpFfmpegArgs(),
        ...cookieArgs(),
        playlist ? '--yes-playlist' : '--no-playlist'
    ];
    if (playlist) {
        args.push('--flat-playlist', '--playlist-end', String(PLAYLIST_CAP));
    }
    args.push('--', query);

    try {
        const { stdout } = await execFileAsync('yt-dlp', args, {
            timeout: playlist ? PLAYLIST_TIMEOUT_MS : META_TIMEOUT_MS,
            maxBuffer: 20 * 1024 * 1024,
            windowsHide: true
        });
        return stdout
            .split('\n')
            .map(line => line.trim())
            .filter(Boolean)
            .map(parseTrack)
            .filter(Boolean);
    } catch (err) {
        const detail = (err.stderr || err.message || '').toString().trim().slice(-400);
        console.error('[music] yt-dlp metadata failed:', detail || err.message);
        return [];
    }
}

function stripPlaylistTitle(track) {
    const song = { ...track };
    delete song.playlistTitle;
    return song;
}

async function spotifySearchTerm(spotifyUrl) {
    try {
        const endpoint = 'https://open.spotify.com/oembed?url=' + encodeURIComponent(spotifyUrl);
        const res = await fetch(endpoint, {
            headers: { 'User-Agent': 'SnowflakeBot/2.0' },
            signal: AbortSignal.timeout(8000)
        });
        if (!res.ok) return null;
        const data = await res.json();
        const title = String(data.title || '').trim();
        const author = String(data.author_name || '').trim();
        if (!title) return null;
        return author ? `${author} ${title}` : title;
    } catch (err) {
        console.error('[music] Spotify oEmbed failed:', err.message);
        return null;
    }
}

async function resolveSearch(term) {
    const youtube = `ytsearch1:${term}`;
    const soundcloud = `scsearch1:${term}`;
    const [primary, fallback] = isLocalRuntime()
        ? [youtube, soundcloud]
        : [soundcloud, youtube];

    const first = await dumpTracks(primary, { playlist: false });
    if (first.length) return { songs: first.map(stripPlaylistTitle), playlistTitle: null };

    const second = await dumpTracks(fallback, { playlist: false });
    return { songs: second.map(stripPlaylistTitle), playlistTitle: null };
}

async function resolveUrl(url) {
    const playlist = isPlaylistUrl(url);
    const tracks = await dumpTracks(url, { playlist });
    if (!tracks.length && playlist) {
        const single = await dumpTracks(url, { playlist: false });
        return { songs: single.map(stripPlaylistTitle), playlistTitle: null };
    }
    const playlistTitle = playlist && tracks.length > 1
        ? (tracks.find(t => t.playlistTitle)?.playlistTitle || 'Playlist')
        : null;
    return { songs: tracks.map(stripPlaylistTitle), playlistTitle };
}

export async function resolveQuery(query) {
    const trimmed = String(query || '').trim();
    if (!trimmed) return { songs: [], playlistTitle: null };

    const spotifyUrl = parseSpotifyTrackUrl(trimmed);
    if (spotifyUrl) {
        const term = await spotifySearchTerm(spotifyUrl);
        if (!term) return { songs: [], playlistTitle: null };
        return resolveSearch(term);
    }

    if (isHttpUrl(trimmed)) return resolveUrl(trimmed);
    if (/^(yt|sc)search\d*:/i.test(trimmed)) {
        const tracks = await dumpTracks(trimmed, { playlist: false });
        return { songs: tracks.map(stripPlaylistTitle), playlistTitle: null };
    }
    return resolveSearch(trimmed);
}

export async function searchSong(query) {
    const { songs } = await resolveQuery(query);
    return songs[0] || null;
}

export function formatDuration(seconds, isLive, guildId) {
    if (isLive) return MessagesService.get(guildId, 'Musica:EnVivo');
    if (seconds == null || !Number.isFinite(seconds)) return '--:--';
    const total = Math.max(0, Math.floor(seconds));
    const hours = Math.floor(total / 3600);
    const minutes = Math.floor((total % 3600) / 60);
    const secs = total % 60;
    if (hours > 0) {
        return `${hours}:${String(minutes).padStart(2, '0')}:${String(secs).padStart(2, '0')}`;
    }
    return `${minutes}:${String(secs).padStart(2, '0')}`;
}

export function songVars(song, guildId) {
    return {
        titulo: song.title || 'Unknown',
        autor: song.author || 'Unknown',
        duracion: formatDuration(song.duration, song.isLive, guildId)
    };
}

export function parseVolume(input, current) {
    const entrada = String(input ?? '').trim();
    if (!entrada) return null;

    if (/^[+-]\d+$/.test(entrada)) {
        return clampVolume(current + parseInt(entrada, 10));
    }
    if (/^\d+$/.test(entrada)) {
        return clampVolume(parseInt(entrada, 10));
    }

    for (const op of ['+', '-', '*', '/']) {
        const idx = entrada.indexOf(op);
        if (idx <= 0 || idx >= entrada.length - 1) continue;
        const a = Number(entrada.slice(0, idx).trim());
        const b = Number(entrada.slice(idx + 1).trim());
        if (!Number.isFinite(a) || !Number.isFinite(b)) continue;
        if (op === '/' && b === 0) return null;
        const result = op === '+' ? a + b : op === '-' ? a - b : op === '*' ? a * b : a / b;
        return clampVolume(result);
    }
    return null;
}

export function parseLoopDuration(input) {
    const raw = String(input ?? '').trim();
    if (!raw) return null;
    const short = parseShortDuration(raw.toLowerCase());
    if (short != null && short > 0) return short;
    const seconds = parseTimestamp(raw);
    if (seconds != null && seconds > 0) return Math.round(seconds * 1000);
    return null;
}

export function parseTimestamp(input) {
    const entrada = String(input ?? '').trim();
    if (!entrada) return null;
    if (/^\d+(\.\d+)?$/.test(entrada)) {
        const n = Number(entrada);
        return n >= 0 ? n : null;
    }
    const parts = entrada.split(':');
    if (parts.length === 2) {
        const min = parseInt(parts[0], 10);
        const sec = Number(parts[1]);
        if (Number.isFinite(min) && Number.isFinite(sec) && min >= 0 && sec >= 0) {
            return min * 60 + sec;
        }
    }
    if (parts.length === 3) {
        const hours = parseInt(parts[0], 10);
        const min = parseInt(parts[1], 10);
        const sec = Number(parts[2]);
        if ([hours, min, sec].every(n => Number.isFinite(n) && n >= 0)) {
            return hours * 3600 + min * 60 + sec;
        }
    }
    return null;
}

function pitchRateFor(pitch) {
    if (pitch === 'nightcore') return 1.25;
    if (pitch === 'daycore') return 0.8;
    return 1;
}

export function currentPosition(guildId) {
    const queue = getQueue(guildId);
    if (!queue) return 0;
    const offset = queue.seekOffset || 0;
    // Rate of the stream that is actually playing, not a pitch we just toggled.
    const rate = queue.streamRate || 1;
    const playedMs = queue.resource?.playbackDuration;
    if (Number.isFinite(playedMs) && playedMs >= 0) {
        return Math.max(0, offset + (playedMs / 1000) * rate);
    }
    if (!queue.startedAt) return offset;
    let elapsedMs = Date.now() - queue.startedAt - (queue.pausedAccum || 0);
    if (queue.pausedAt) elapsedMs -= Date.now() - queue.pausedAt;
    return Math.max(0, offset + (elapsedMs / 1000) * rate);
}

function audioFilters(queue) {
    const filters = [];
    // Normalize to 48 kHz first so asetrate 1.25 / 0.80 matches streamRate.
    if (queue.pitch === 'nightcore') {
        filters.push('aformat=sample_rates=48000', 'asetrate=48000*1.25', 'aresample=48000');
    } else if (queue.pitch === 'daycore') {
        filters.push('aformat=sample_rates=48000', 'asetrate=48000*0.80', 'aresample=48000');
    }
    if (queue.bass > 0) {
        const gain = (queue.bass / 100) * 15;
        filters.push(`bass=g=${gain.toFixed(2)}`);
    }
    return filters;
}

function killPipeline(queue) {
    const procs = queue?.procs || [];
    queue.procs = [];
    queue.extractor = null;
    for (const proc of procs) {
        if (!proc || proc.killed) continue;
        try { proc.stdout?.destroy(); } catch { /* ignore */ }
        try { proc.stdin?.destroy(); } catch { /* ignore */ }
        try { proc.kill('SIGKILL'); } catch { /* ignore */ }
    }
}

function startPipeline(song, queue) {
    const start = Math.max(0, queue.seekOffset || 0);
    let end = null;
    if (queue.loopMode === 'ab' && queue.abEnd != null && queue.abEnd > start) {
        end = queue.abEnd;
    }
    const filters = audioFilters(queue);
    const procs = [];

    const ffmpegOut = ['-f', 's16le', '-ar', '48000', '-ac', '2', 'pipe:1'];
    const filterArgs = filters.length ? ['-filter:a', filters.join(',')] : [];

    if (song.isAttachment || isDirectAudioUrl(song.url)) {
        const args = ['-hide_banner', '-loglevel', 'error'];
        if (start > 0) args.push('-ss', String(start));
        args.push('-i', song.url);
        if (end != null) args.push('-t', String(Math.max(0.1, end - start)));
        args.push(...filterArgs, ...ffmpegOut);
        const ff = spawn(ffmpegBin, args, { stdio: ['ignore', 'pipe', 'pipe'] });
        drain(ff.stderr);
        ff.on('error', err => console.error('[music] ffmpeg spawn failed:', err.message));
        ff.on('exit', (code, signal) => {
            if (signal) console.error(`[music] ffmpeg killed by ${signal}`);
        });
        procs.push(ff);
        return { stream: ff.stdout, procs };
    }

    const ytdlpArgs = [
        '-o', '-',
        '-f', 'bestaudio/best',
        '--no-playlist',
        '--no-warnings',
        '--no-progress',
        '--no-part',
        ...ytDlpFfmpegArgs(),
        ...cookieArgs()
    ];
    if (start > 0 || end != null) {
        const sectionEnd = end == null ? 'inf' : String(end);
        ytdlpArgs.push('--download-sections', `*${start}-${sectionEnd}`, '--force-keyframes-at-cuts');
    }
    ytdlpArgs.push('--', song.url);

    const yt = spawn('yt-dlp', ytdlpArgs, { stdio: ['ignore', 'pipe', 'pipe'] });
    drain(yt.stderr);
    yt.on('error', err => console.error('[music] yt-dlp spawn failed:', err.message));

    const ffArgs = ['-hide_banner', '-loglevel', 'error', '-i', 'pipe:0', ...filterArgs, ...ffmpegOut];
    const ff = spawn(ffmpegBin, ffArgs, { stdio: ['pipe', 'pipe', 'pipe'] });
    drain(ff.stderr);
    ff.on('error', err => console.error('[music] ffmpeg spawn failed:', err.message));
    ff.on('exit', (code, signal) => {
        if (signal) console.error(`[music] ffmpeg killed by ${signal}`);
    });
    yt.stdout.pipe(ff.stdin);
    yt.stdout.on('error', () => {});
    ff.stdin.on('error', () => {});

    procs.push(yt, ff);
    return { stream: ff.stdout, procs };
}

function bindPlayer(guildId, queue) {
    queue.player.on(AudioPlayerStatus.Idle, () => {
        if (queue.stopping) return;
        killPipeline(queue);

        if (queue.restarting) {
            queue.restarting = false;
            queue.skipping = false;
            if (queue.songs.length === 0) {
                destroyQueue(guildId, { silent: true });
                return;
            }
            void playNext(guildId);
            return;
        }

        if (queue.loopMode === 'ab' && queue.abStart != null && queue.abEnd != null && !queue.skipping) {
            if (consumeLoop(queue, 'ab')) {
                queue.seekOffset = queue.abStart;
                void playNext(guildId).then(() => ui.onTrackChange(guildId, queue.textChannel));
                return;
            }
            queue.loopMode = 'off';
            queue.abStart = null;
            queue.abEnd = null;
        }

        if (queue.loopMode === 'track' && !queue.skipping) {
            if (consumeLoop(queue, 'track')) {
                queue.seekOffset = 0;
                void playNext(guildId).then(() => ui.onTrackChange(guildId, queue.textChannel));
                return;
            }
            queue.loopMode = 'off';
        }

        const finished = queue.songs.shift();
        if (queue.loopMode === 'queue' && finished && !queue.skipping) {
            if (consumeLoop(queue, 'queue')) {
                queue.songs.push(finished);
            } else {
                queue.loopMode = 'off';
            }
        }
        queue.seekOffset = 0;
        queue.abStart = null;
        queue.abEnd = null;
        if (queue.loopMode === 'ab') queue.loopMode = 'off';

        const fromSkip = queue.skipping;
        queue.skipping = false;

        if (queue.songs.length === 0) {
            destroyQueue(guildId, { silent: true });
            return;
        }
        void playNext(guildId).then(() => {
            if (!fromSkip) ui.onTrackChange(guildId, queue.textChannel);
        });
    });

    queue.player.on('error', error => {
        console.error('[music] player error:', error.message);
        if (queue.stopping || queue.skipping || queue.restarting) return;
        queue.textChannel?.send(MessagesService.get(guildId, 'Musica:ErrorLavalink')).catch(() => {});
    });
}

export function getQueue(guildId) {
    return queues.get(guildId);
}

function createQueue(guildId, voiceChannel, textChannel) {
    const queue = {
        guildId,
        voiceChannel,
        textChannel,
        connection: null,
        player: createAudioPlayer({
            behaviors: { noSubscriber: NoSubscriberBehavior.Play }
        }),
        songs: [],
        playing: false,
        volume: savedVolumePercent(guildId) / 100,
        resource: null,
        procs: [],
        extractor: null,
        stopping: false,
        skipping: false,
        restarting: false,
        generation: 0,
        pitch: 'off',
        bass: 0,
        loopMode: 'off',
        loopRepeatsLeft: null,
        loopUntil: null,
        loopCycleSize: 0,
        loopWrapsInCycle: 0,
        abStart: null,
        abEnd: null,
        seekOffset: 0,
        streamRate: 1,
        startedAt: 0,
        pausedAt: null,
        pausedAccum: 0
    };
    bindPlayer(guildId, queue);
    queues.set(guildId, queue);
    return queue;
}

export function destroyQueue(guildId, { silent = true } = {}) {
    const queue = queues.get(guildId);
    if (!queue) return;
    const channel = queue.textChannel;
    queue.stopping = true;
    queue.playing = false;
    killPipeline(queue);
    try { queue.player.removeAllListeners(); } catch { /* ignore */ }
    try { queue.player.stop(true); } catch { /* ignore */ }
    try {
        if (queue.connection && queue.connection.state.status !== VoiceConnectionStatus.Destroyed) {
            queue.connection.destroy();
        }
    } catch { /* ignore */ }
    queues.delete(guildId);
    ui.onStopped(guildId, channel);
    if (!silent && channel) {
        channel.send(MessagesService.get(guildId, 'Musica:ColaVacia')).catch(() => {});
    }
}

export function stopPlayback(guildId) {
    destroyQueue(guildId, { silent: true });
}

export async function ensureQueue(ctx) {
    const guildId = guildIdOf(ctx);
    const voiceChannel = ctx.member?.voice?.channel;
    if (!voiceChannel) {
        const err = new Error('not in a voice channel');
        err.code = 'NO_VOICE';
        throw err;
    }

    let queue = getQueue(guildId);
    const existing = getVoiceConnection(guildId);
    const alive = existing && existing.state.status !== VoiceConnectionStatus.Destroyed;
    if (queue && alive) {
        queue.voiceChannel = voiceChannel;
        if (ctx.channel) queue.textChannel = ctx.channel;
        return queue;
    }

    if (queue) destroyQueue(guildId, { silent: true });
    queue = createQueue(guildId, voiceChannel, ctx.channel);

    const connection = joinVoiceChannel({
        channelId: voiceChannel.id,
        guildId,
        adapterCreator: ctx.guild.voiceAdapterCreator,
        selfDeaf: true
    });
    queue.connection = connection;
    connection.subscribe(queue.player);

    connection.on(VoiceConnectionStatus.Disconnected, async () => {
        try {
            await Promise.race([
                entersState(connection, VoiceConnectionStatus.Signalling, 5_000),
                entersState(connection, VoiceConnectionStatus.Connecting, 5_000)
            ]);
        } catch {
            destroyQueue(guildId, { silent: true });
        }
    });

    connection.on(VoiceConnectionStatus.Destroyed, () => {
        if (!queue.stopping) destroyQueue(guildId, { silent: true });
    });

    await entersState(connection, VoiceConnectionStatus.Ready, 20_000);
    return queue;
}

export async function playNext(guildId) {
    const queue = getQueue(guildId);
    if (!queue || queue.stopping) return;

    const token = ++queue.generation;
    if (queue.songs.length === 0) {
        destroyQueue(guildId, { silent: true });
        return;
    }

    const song = queue.songs[0];
    try {
        killPipeline(queue);
        const { stream, procs } = startPipeline(song, queue);
        queue.procs = procs;

        const resource = createAudioResource(stream, {
            inputType: StreamType.Raw,
            inlineVolume: true
        });

        if (queue.generation !== token || queue.stopping) {
            killPipeline(queue);
            return;
        }

        resource.volume.setVolume(queue.volume);
        queue.resource = resource;
        queue.playing = true;
        queue.streamRate = pitchRateFor(queue.pitch);
        queue.startedAt = Date.now();
        queue.pausedAt = null;
        queue.pausedAccum = 0;
        queue.player.play(resource);
    } catch (error) {
        console.error('[music] play failed:', error);
        if (queue.generation !== token || queue.stopping) return;
        killPipeline(queue);
        queue.songs.shift();
        queue.seekOffset = 0;
        queue.textChannel?.send(MessagesService.get(guildId, 'Musica:ErrorLavalink')).catch(() => {});
        await playNext(guildId);
    }
}

function restartCurrent(queue) {
    queue.restarting = true;
    const stopped = queue.player.stop(true);
    if (!stopped) {
        queue.restarting = false;
        void playNext(queue.guildId);
    }
}

export function skipCurrent(guildId) {
    const queue = getQueue(guildId);
    if (!queue) return null;
    queue.skipping = true;
    queue.abStart = null;
    queue.abEnd = null;
    if (queue.loopMode === 'ab') queue.loopMode = 'off';
    queue.songs.shift();
    const next = queue.songs[0] || null;
    queue.seekOffset = 0;
    queue.restarting = true;
    const stopped = queue.player.stop(true);
    if (!stopped) {
        queue.restarting = false;
        queue.skipping = false;
        if (!next) destroyQueue(guildId, { silent: true });
        else void playNext(guildId);
    }
    return next;
}

export function pausePlayback(guildId) {
    const queue = getQueue(guildId);
    if (!queue) return false;
    const ok = queue.player.pause();
    if (ok) queue.pausedAt = Date.now();
    return ok;
}

export function resumePlayback(guildId) {
    const queue = getQueue(guildId);
    if (!queue) return false;
    if (queue.pausedAt) {
        queue.pausedAccum += Date.now() - queue.pausedAt;
        queue.pausedAt = null;
    }
    return queue.player.unpause();
}

export function isPaused(guildId) {
    const queue = getQueue(guildId);
    if (!queue) return false;
    const status = queue.player.state.status;
    return status === AudioPlayerStatus.Paused || status === AudioPlayerStatus.AutoPaused;
}

export function getVolumePercent(guildId) {
    const queue = getQueue(guildId);
    if (queue) return clampVolume(queue.volume * 100);
    return savedVolumePercent(guildId);
}

export function setVolume(guildId, percent) {
    const level = clampVolume(percent);
    updateGuildConfig(guildId, { Volume: level });
    const queue = getQueue(guildId);
    if (queue) {
        queue.volume = level / 100;
        try { queue.resource?.volume?.setVolume(queue.volume); } catch { /* ignore */ }
    }
    return level;
}

export function applyVolumeInput(guildId, input) {
    const parsed = parseVolume(input, getVolumePercent(guildId));
    if (parsed == null) return { ok: false };
    return { ok: true, level: setVolume(guildId, parsed) };
}

export function shuffleQueue(guildId) {
    const queue = getQueue(guildId);
    if (!queue || queue.songs.length < 2) return false;
    const [current, ...rest] = queue.songs;
    for (let i = rest.length - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1));
        [rest[i], rest[j]] = [rest[j], rest[i]];
    }
    queue.songs = [current, ...rest];
    return true;
}

export function seekTo(guildId, seconds) {
    const queue = getQueue(guildId);
    const song = queue?.songs[0];
    if (!queue || !song) return { ok: false, reason: 'inactive' };
    if (song.isLive) return { ok: false, reason: 'range' };
    if (seconds < 0 || (song.duration != null && seconds > song.duration)) {
        return { ok: false, reason: 'range' };
    }
    queue.seekOffset = seconds;
    restartCurrent(queue);
    return { ok: true, seconds };
}

export function replayCurrent(guildId) {
    return seekTo(guildId, 0);
}

function clearLoopLimits(queue) {
    queue.loopRepeatsLeft = null;
    queue.loopUntil = null;
    queue.loopCycleSize = 0;
    queue.loopWrapsInCycle = 0;
}

function consumeLoop(queue, kind) {
    if (queue.loopUntil && Date.now() >= queue.loopUntil) {
        clearLoopLimits(queue);
        return false;
    }

    if (kind === 'queue' && queue.loopRepeatsLeft != null) {
        queue.loopWrapsInCycle = (queue.loopWrapsInCycle || 0) + 1;
        const size = Math.max(1, queue.loopCycleSize || queue.songs.length || 1);
        if (queue.loopWrapsInCycle < size) return true;
        queue.loopWrapsInCycle = 0;
    }

    if (queue.loopRepeatsLeft != null) {
        if (queue.loopRepeatsLeft <= 0) {
            clearLoopLimits(queue);
            return false;
        }
        queue.loopRepeatsLeft -= 1;
    }
    return true;
}

export function setLoopMode(guildId, mode, { repeats, durationMs } = {}) {
    const queue = getQueue(guildId);
    if (!queue) return null;
    if (!['off', 'track', 'queue'].includes(mode)) return { mode: queue.loopMode };

    queue.loopMode = mode;
    queue.abStart = null;
    queue.abEnd = null;
    clearLoopLimits(queue);

    if (mode === 'off') return { mode: 'off' };

    if (repeats != null) queue.loopRepeatsLeft = repeats;
    if (durationMs != null) queue.loopUntil = Date.now() + durationMs;
    queue.loopCycleSize = Math.max(1, queue.songs.length);
    queue.loopWrapsInCycle = 0;
    return {
        mode,
        repeats: queue.loopRepeatsLeft,
        until: queue.loopUntil
    };
}

export function formatLoopReply(guildId, result) {
    if (!result || result.mode === 'off') {
        return MessagesService.get(guildId, 'Musica:LoopOff');
    }
    const base = result.mode === 'queue'
        ? MessagesService.get(guildId, 'Musica:LoopCola')
        : MessagesService.get(guildId, 'Musica:LoopPista');
    const extras = [];
    if (result.repeats != null) {
        extras.push(MessagesService.get(guildId, 'Musica:LoopLimiteVeces', { n: result.repeats }));
    }
    if (result.until) {
        extras.push(MessagesService.get(guildId, 'Musica:LoopLimiteTiempo', {
            duracion: formatDuration((result.until - Date.now()) / 1000, false, guildId)
        }));
    }
    if (!extras.length) return base;
    return `${base} ${extras.join(' ')}`;
}

export function cycleLoopMode(guildId) {
    const queue = getQueue(guildId);
    if (!queue) return null;
    const current = LOOP_CYCLE.includes(queue.loopMode) ? queue.loopMode : 'off';
    const next = LOOP_CYCLE[(LOOP_CYCLE.indexOf(current) + 1) % LOOP_CYCLE.length];
    return setLoopMode(guildId, next);
}

export function setLoopAb(guildId, action, position) {
    const queue = getQueue(guildId);
    if (!queue?.songs[0]) return { ok: false, reason: 'inactive' };
    if (queue.songs[0].isLive) return { ok: false, reason: 'range' };

    if (action === 'off') {
        const wasAb = queue.loopMode === 'ab';
        queue.abStart = null;
        queue.abEnd = null;
        if (queue.loopMode === 'ab') queue.loopMode = 'off';
        if (wasAb) restartCurrent(queue);
        return { ok: true, action: 'off' };
    }

    const pos = position == null ? currentPosition(guildId) : position;
    if (pos == null || pos < 0) return { ok: false, reason: 'timestamp' };

    if (action === 'set-a') {
        queue.abStart = pos;
        if (queue.abEnd != null && queue.abEnd <= queue.abStart) queue.abEnd = null;
        return { ok: true, action: 'set-a', position: pos };
    }

    if (action === 'set-b') {
        if (queue.abStart == null) return { ok: false, reason: 'need-a' };
        if (pos <= queue.abStart) return { ok: false, reason: 'range' };
        queue.abEnd = pos;
        queue.loopMode = 'ab';
        clearLoopLimits(queue);
        queue.seekOffset = queue.abStart;
        restartCurrent(queue);
        return { ok: true, action: 'set-b', position: pos, start: queue.abStart, end: queue.abEnd };
    }

    return { ok: false, reason: 'timestamp' };
}

export function setPitch(guildId, pitch) {
    const queue = getQueue(guildId);
    if (!queue) return null;
    const next = pitch === 'toggle-nightcore'
        ? (queue.pitch === 'nightcore' ? 'off' : 'nightcore')
        : pitch === 'toggle-daycore'
            ? (queue.pitch === 'daycore' ? 'off' : 'daycore')
            : pitch;
    if (!['off', 'nightcore', 'daycore'].includes(next)) return queue.pitch;
    const changed = queue.pitch !== next;
    if (changed && queue.playing) {
        queue.seekOffset = currentPosition(guildId);
    }
    queue.pitch = next;
    if (changed && queue.playing) restartCurrent(queue);
    return queue.pitch;
}

export function setBass(guildId, intensity) {
    const queue = getQueue(guildId);
    if (!queue) return null;
    const next = Math.max(0, Math.min(100, Math.round(Number(intensity) || 0)));
    const changed = queue.bass !== next;
    if (changed && queue.playing) {
        queue.seekOffset = currentPosition(guildId);
    }
    queue.bass = next;
    if (changed && queue.playing) restartCurrent(queue);
    return queue.bass;
}

export function getPlaybackState(guildId) {
    const queue = getQueue(guildId);
    if (!queue) return null;
    return {
        song: queue.songs[0] || null,
        upcoming: queue.songs.slice(1),
        paused: isPaused(guildId),
        volume: getVolumePercent(guildId),
        pitch: queue.pitch,
        bass: queue.bass,
        loopMode: queue.loopMode,
        loopRepeatsLeft: queue.loopRepeatsLeft,
        loopUntil: queue.loopUntil,
        abStart: queue.abStart,
        abEnd: queue.abEnd,
        position: currentPosition(guildId),
        playing: queue.playing
    };
}

export function buildQueueEmbed(guildId) {
    const queue = getQueue(guildId);
    const current = queue?.songs[0];
    if (!queue || !current) return null;

    const embed = new EmbedBuilder()
        .setTitle(MessagesService.get(guildId, 'Musica:ColaTitulo'))
        .setColor('Blurple');

    embed.addFields({
        name: MessagesService.get(guildId, 'Musica:SonandoAhora'),
        value: `**[${current.title}](${current.url})** — ${current.author}`
    });

    const upcoming = queue.songs.slice(1);
    if (upcoming.length) {
        let total = 0;
        const lines = upcoming.map((song, i) => {
            if (!song.isLive && Number.isFinite(song.duration)) total += song.duration;
            return `\`${String(i + 1).padStart(2, ' ')}.\` **${song.title}** — ${song.author}`;
        });
        let body = lines.join('\n');
        if (body.length > 1900) body = body.slice(0, 1897) + '…';
        embed.addFields({
            name: MessagesService.get(guildId, 'Musica:ColaSiguiente'),
            value: body
        });
        if (total > 0) {
            embed.addFields({
                name: MessagesService.get(guildId, 'Musica:ColaTotal'),
                value: formatDuration(total, false, guildId),
                inline: true
            });
        }
    }
    return embed;
}

export function dspLabel(guildId) {
    const queue = getQueue(guildId);
    if (!queue) return '';
    const parts = [];
    if (queue.loopMode === 'track' || queue.loopMode === 'queue' || queue.loopMode === 'ab') {
        let loopTag = queue.loopMode === 'queue' ? '🔁📃' : queue.loopMode === 'ab' ? '🔁A-B' : '🔁';
        if (queue.loopRepeatsLeft != null) loopTag += `×${queue.loopRepeatsLeft}`;
        if (queue.loopUntil) {
            const left = Math.max(0, (queue.loopUntil - Date.now()) / 1000);
            loopTag += ` ${formatDuration(left, false, guildId)}`;
        }
        parts.push(loopTag);
    }
    if (queue.pitch === 'nightcore') parts.push('Nightcore');
    if (queue.pitch === 'daycore') parts.push('Daycore');
    if (queue.bass > 0) parts.push(`Bass ${queue.bass}%`);
    parts.push(`${getVolumePercent(guildId)}%`);
    return parts.join(' · ');
}

export function canControlMusic(ctx) {
    const guildId = guildIdOf(ctx);
    const member = ctx.member;
    if (member?.permissions?.has(PermissionFlagsBits.ManageGuild)) return { ok: true };

    const dj = getGuildConfig(guildId)?.DjRoleId;
    if (dj && member?.roles?.cache?.has(String(dj))) return { ok: true };

    const botChannel = ctx.guild?.members?.me?.voice?.channelId;
    const userChannel = member?.voice?.channelId;
    if (botChannel && userChannel && botChannel === userChannel) return { ok: true };

    const message = dj
        ? MessagesService.get(guildId, 'Musica:RequiereDj', { rol: `<@&${dj}>` })
        : MessagesService.get(guildId, 'Musica:MismoCanal');
    return { ok: false, message };
}

export const AUDIO_EXTENSIONS = ['.mp3', '.wav', '.flac', '.ogg', '.oga', '.m4a', '.aac', '.webm', '.opus'];
