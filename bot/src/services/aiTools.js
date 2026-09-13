import db from './database.js';
import { ChannelType, PermissionFlagsBits } from 'discord.js';
import { registerIncident, announceIncident, notifyMemberDm, IncidentType } from './moderationLog.js';
import MessagesService from './messagesService.js';
import { setAfk, removeAfk, listAfk } from './afk.js';
import { changeMemberRole } from './roles.js';
import { scheduleTimedRole, cancelTimedRole } from './timedRoles.js';
import { parseDuration, formatCompactDuration, MAX_TEMP_ROLE_MS } from '../lib/duration.js';
import { lockChannel, unlockChannel, isLockableChannel } from './channelLock.js';
import { hierarchyError, defaultReason } from '../lib/moderation.js';
import { performHardmute, scheduleUnhardmute, performUnhardmute } from './hardmuteManager.js';
import { updateGuildConfig } from './guildConfig.js';
import { formatNumber } from './countingService.js';
import { installPalette, uninstallPalette } from './colorService.js';
import { resolveChannel as resolveYouTubeChannel, getLatestVideo } from './youtubeService.js';
import {
    getQueue,
    getPlaybackState,
    ensureQueue,
    playNext,
    resolveQuery,
    songVars,
    canControlMusic,
    skipCurrent,
    stopPlayback,
    pausePlayback,
    resumePlayback,
    applyVolumeInput,
    shuffleQueue,
    seekTo,
    replayCurrent,
    setLoopMode,
    formatLoopReply,
    parseLoopDuration,
    setLoopAb,
    setPitch,
    setBass,
    parseTimestamp,
    formatDuration
} from './music.js';

export { parseDuration } from '../lib/duration.js';

export function resolveMember(guild, userArg) {
    if (!userArg) return null;
    const mention = userArg.replace(/[<@!>]/g, '').trim();
    return guild.members.cache.get(mention)
        || guild.members.cache.find(m =>
            m.user.username.toLowerCase() === userArg.toLowerCase() ||
            m.displayName.toLowerCase() === userArg.toLowerCase()
        ) || null;
}

export function resolveChannel(guild, channelArg) {
    if (!channelArg) return null;
    const id = channelArg.replace(/[<#>]/g, '').trim();
    return guild.channels.cache.get(id)
        || guild.channels.cache.find(c => c.name.toLowerCase() === channelArg.toLowerCase())
        || null;
}

export function resolveRole(guild, roleArg) {
    if (!roleArg) return null;
    const id = roleArg.replace(/[<@&>]/g, '').trim();
    return guild.roles.cache.get(id)
        || guild.roles.cache.find(r => r.name.toLowerCase() === roleArg.toLowerCase())
        || null;
}

async function resolveMemberAsync(guild, userArg) {
    const cached = resolveMember(guild, userArg);
    if (cached) return cached;
    const id = String(userArg || '').replace(/[<@!>]/g, '').trim();
    if (/^\d{17,20}$/.test(id)) return guild.members.fetch(id).catch(() => null);
    return null;
}

function channelOf(ctx, arg) {
    if (!arg || String(arg).trim().toLowerCase() === 'current') return ctx.channel;
    return resolveChannel(ctx.guild, arg) || ctx.channel;
}

function failPerm(ctx, permission, description) {
    if (!ctx.member.permissions.has(permission)) {
        return { success: false, text: MessagesService.get(ctx.guild.id, 'Errores:SinPermisos'), description };
    }
    return null;
}

function failHierarchy(ctx, member, description) {
    const err = hierarchyError({
        guildId: ctx.guild.id,
        guild: ctx.guild,
        user: ctx.member.user,
        member: ctx.member
    }, member);
    if (err) return { success: false, text: err, description };
    return null;
}

function ensureCountingRow(guildId) {
    db.prepare(`
        INSERT OR IGNORE INTO CountingConfigs
        (GuildId, CurrentValue, CurrentRecord, RecordAtChainStart, RecordCelebratedThisChain, Base, ExtraChancesPerDay, ExtraChancesUsedToday)
        VALUES (?, 0, 0, 0, 0, 'Decimal', 0, 0)
    `).run(guildId);
}

function requireMusicControl(ctx, { playing = true, description = '/music' } = {}) {
    const guildId = ctx.guild.id;
    const access = canControlMusic(ctx);
    if (!access.ok) return { success: false, text: access.message, description };
    const queue = getQueue(guildId);
    if (!queue || (playing && !queue.playing)) {
        return { success: false, text: MessagesService.get(guildId, 'Musica:NoActivo'), description };
    }
    return null;
}

// ──────────────────────────────────────────────
// Tool definitions
// ──────────────────────────────────────────────

export const tools = [
    // ── SERVER STATE (read-only) ──
    {
        name: 'get_server_state',
        description: 'Get current server bot settings: language, AI toggles, welcome config, music player state, volume.',
        parameters: { type: 'object', properties: {} },
        destructive: false,
        async execute(ctx) {
            const row = db.prepare('SELECT * FROM GuildConfigs WHERE GuildId = ?').get(ctx.guild.id);
            const playback = getPlaybackState(ctx.guild.id);
            const state = {
                guildName: ctx.guild.name,
                language: row?.Language || 'en',
                aiChatEnabled: row?.AiChatEnabled !== 0,
                aiMentionsEnabled: row?.AiMentionsEnabled === 1,
                aiWebSearchEnabled: row?.AiWebSearchEnabled !== 0,
                aiCommandsEnabled: row?.AiCommandsEnabled !== 0,
                volume: playback?.volume ?? row?.Volume ?? 100,
                djRoleId: row?.DjRoleId?.toString() || null,
                welcomeChannelId: row?.WelcomeChannelId?.toString() || null,
                welcomeMessage: row?.WelcomeMessage || null,
                music: playback
                    ? {
                        playing: playback.song?.title || null,
                        author: playback.song?.author || null,
                        paused: playback.paused,
                        queueLength: (playback.upcoming?.length || 0) + (playback.song ? 1 : 0),
                        loop: playback.loopMode,
                        pitch: playback.pitch,
                        bass: playback.bass
                    }
                    : { playing: null }
            };
            return {
                success: true,
                text: JSON.stringify(state, null, 2),
                description: 'Consultar estado del servidor'
            };
        }
    },

    // ── MODERATION ──
    {
        name: 'warn_user',
        description: 'Record a warning for a user and send them a DM.',
        parameters: {
            type: 'object',
            properties: {
                user: { type: 'string', description: 'User mention, ID or username' },
                reason: { type: 'string', description: 'Reason for the warning' }
            },
            required: ['user', 'reason']
        },
        destructive: true,
        requiredPermissions: [PermissionFlagsBits.ModerateMembers],
        describe: async (ctx, args) => `Advertir a ${args.user} por "${args.reason || 'Sin motivo'}"`,
        async execute(ctx, args) {
            if (!ctx.member.permissions.has(PermissionFlagsBits.ModerateMembers)) {
                return { success: false, text: 'No tienes permisos de moderación para advertir miembros.', description: 'Advertir usuario' };
            }

            const member = resolveMember(ctx.guild, args.user);
            if (!member) return { success: false, text: `No encontré al usuario: ${args.user}`, description: `Advertir usuario` };

            registerIncident(ctx.guild.id, member.user, ctx.member.user, 'Advertencia', args.reason);

            try {
                await member.send(`⚠️ Has recibido una advertencia en **${ctx.guild.name}**.\nMotivo: ${args.reason}`);
            } catch { /* DMs closed */ }

            return {
                success: true,
                text: `✅ Advertencia registrada a **${member.user.tag}**.\nMotivo: ${args.reason}`,
                description: `Advertir a @${member.displayName}`
            };
        }
    },
    {
        name: 'timeout_user',
        description: 'Timeout (isolate) a user for a duration. Duration format: 30s, 10m, 2h, 7d (max 28d).',
        parameters: {
            type: 'object',
            properties: {
                user: { type: 'string', description: 'User mention, ID or username' },
                duration: { type: 'string', description: 'Duration e.g. 10m, 1h, 7d' },
                reason: { type: 'string', description: 'Reason for the timeout' }
            },
            required: ['user', 'duration']
        },
        destructive: true,
        requiredPermissions: [PermissionFlagsBits.ModerateMembers],
        describe: async (ctx, args) => `Aislar a ${args.user} durante ${args.duration} por "${args.reason || 'Sin motivo'}"`,
        async execute(ctx, args) {
            if (!ctx.member.permissions.has(PermissionFlagsBits.ModerateMembers)) {
                return { success: false, text: 'No tienes permisos para aislar miembros.', description: 'Aislar usuario' };
            }

            const member = resolveMember(ctx.guild, args.user);
            if (!member) return { success: false, text: `No encontré al usuario: ${args.user}`, description: 'Aislar usuario' };

            const ms = parseDuration(args.duration);
            if (!ms || ms > 28 * 86400000) {
                return { success: false, text: 'Duración inválida. Usa 30s, 10m, 2h, 7d (máx 28 días).', description: `Aislar a @${member.displayName}` };
            }

            if (!member.moderatable) {
                return { success: false, text: `No tengo jerarquía suficiente para aislar a **${member.displayName}**.`, description: 'Aislar usuario' };
            }

            await member.timeout(ms, args.reason || 'Aislamiento vía comando IA');
            registerIncident(ctx.guild.id, member.user, ctx.member.user, 'Silencio', args.reason, args.duration);

            return {
                success: true,
                text: `✅ **${member.user.tag}** ha sido aislado por **${args.duration}**.\nMotivo: ${args.reason || 'Sin motivo'}`,
                description: `Aislar a @${member.displayName}`
            };
        }
    },
    {
        name: 'kick_user',
        description: 'Kick a user from the server.',
        parameters: {
            type: 'object',
            properties: {
                user: { type: 'string', description: 'User mention, ID or username' },
                reason: { type: 'string', description: 'Reason for the kick' }
            },
            required: ['user']
        },
        destructive: true,
        requiredPermissions: [PermissionFlagsBits.KickMembers],
        describe: async (ctx, args) => `Expulsar a ${args.user} por "${args.reason || 'Sin motivo'}"`,
        async execute(ctx, args) {
            if (!ctx.member.permissions.has(PermissionFlagsBits.KickMembers)) {
                return { success: false, text: 'No tienes permisos para expulsar miembros.', description: 'Expulsar usuario' };
            }

            const member = resolveMember(ctx.guild, args.user);
            if (!member) return { success: false, text: `No encontré al usuario: ${args.user}`, description: 'Expulsar usuario' };

            if (!member.kickable) {
                return { success: false, text: `No puedo expulsar a **${member.displayName}** (jerarquía superior o permisos insuficientes).`, description: 'Expulsar usuario' };
            }

            await member.kick(args.reason || 'Expulsado vía comando IA');
            registerIncident(ctx.guild.id, member.user, ctx.member.user, 'Expulsion', args.reason);

            return {
                success: true,
                text: `✅ **${member.user.tag}** ha sido expulsado.\nMotivo: ${args.reason || 'Sin motivo'}`,
                description: `Expulsar a @${member.displayName}`
            };
        }
    },
    {
        name: 'ban_user',
        description: 'Ban a user from the server.',
        parameters: {
            type: 'object',
            properties: {
                user: { type: 'string', description: 'User mention, ID or username' },
                reason: { type: 'string', description: 'Reason for the ban' },
                delete_days: { type: 'number', description: 'Days of messages to delete (0-7)' }
            },
            required: ['user']
        },
        destructive: true,
        requiredPermissions: [PermissionFlagsBits.BanMembers],
        describe: async (ctx, args) => `Vetar a ${args.user} por "${args.reason || 'Sin motivo'}"`,
        async execute(ctx, args) {
            if (!ctx.member.permissions.has(PermissionFlagsBits.BanMembers)) {
                return { success: false, text: 'No tienes permisos para vetar miembros.', description: 'Vetar usuario' };
            }

            const member = resolveMember(ctx.guild, args.user);
            if (!member) return { success: false, text: `No encontré al usuario: ${args.user}`, description: 'Vetar usuario' };

            if (!member.bannable) {
                return { success: false, text: `No puedo vetar a **${member.displayName}** (jerarquía superior o permisos insuficientes).`, description: 'Vetar usuario' };
            }

            const deleteDays = Math.min(7, Math.max(0, args.delete_days || 0));
            await ctx.guild.members.ban(member, {
                reason: args.reason || 'Vetado vía comando IA',
                deleteMessageSeconds: deleteDays * 86400
            });
            registerIncident(ctx.guild.id, member.user, ctx.member.user, 'Veto', args.reason);

            return {
                success: true,
                text: `✅ **${member.user.tag}** ha sido vetado del servidor.\nMotivo: ${args.reason || 'Sin motivo'}`,
                description: `Vetar a @${member.displayName}`
            };
        }
    },
    {
        name: 'clear_messages',
        description: 'Bulk delete recent messages from a channel (up to 100).',
        parameters: {
            type: 'object',
            properties: {
                amount: { type: 'number', description: 'Number of messages to delete (1-100)' },
                channel: { type: 'string', description: 'Channel mention/ID (defaults to current channel)' }
            },
            required: ['amount']
        },
        destructive: true,
        requiredPermissions: [PermissionFlagsBits.ManageMessages],
        describe: async (ctx, args) => `Eliminar ${args.amount} mensajes en ${args.channel || 'este canal'}`,
        async execute(ctx, args) {
            if (!ctx.member.permissions.has(PermissionFlagsBits.ManageMessages)) {
                return { success: false, text: 'No tienes permisos para gestionar mensajes.', description: 'Limpiar mensajes' };
            }

            const amount = Math.min(100, Math.max(1, parseInt(args.amount) || 1));
            const channel = args.channel ? resolveChannel(ctx.guild, args.channel) : ctx.channel;
            if (!channel) return { success: false, text: 'Canal no encontrado.', description: 'Limpiar mensajes' };

            const deleted = await channel.bulkDelete(amount, true);
            return {
                success: true,
                text: `✅ Se eliminaron **${deleted.size}** mensajes en <#${channel.id}>.`,
                description: `Limpiar ${amount} mensajes`
            };
        }
    },

    // ── MUSIC ──
    {
        name: 'music_play',
        description: 'Play a song or playlist in the voice channel of the user who is talking (URL or search).',
        parameters: {
            type: 'object',
            properties: {
                query: { type: 'string', description: 'YouTube/Spotify URL or search terms' }
            },
            required: ['query']
        },
        destructive: false,
        async execute(ctx, args) {
            const guildId = ctx.guild.id;
            const desc = '/play';
            if (!args.query?.trim()) {
                return { success: false, text: MessagesService.get(guildId, 'Musica:NoEncontrado'), description: desc };
            }
            if (!ctx.member?.voice?.channel) {
                return { success: false, text: MessagesService.get(guildId, 'Musica:NoEnCanal'), description: desc };
            }
            const resolved = await resolveQuery(args.query);
            if (!resolved.songs.length) {
                return { success: false, text: MessagesService.get(guildId, 'Musica:NoEncontrado'), description: desc };
            }
            let queue;
            try {
                queue = await ensureQueue(ctx);
            } catch (err) {
                console.error('[ai] music_play join failed:', err.message);
                return { success: false, text: MessagesService.get(guildId, 'Musica:ErrorLavalink'), description: desc };
            }
            const startedIdle = !queue.playing && queue.songs.length === 0;
            const first = resolved.songs[0];
            queue.songs.push(...resolved.songs.map(song => ({
                ...song,
                requester: ctx.member.displayName || ctx.member.user?.username
            })));
            if (startedIdle) await playNext(guildId);

            let text;
            if (resolved.playlistTitle) {
                text = MessagesService.get(guildId, 'Musica:PlaylistAnadida', {
                    titulo: resolved.playlistTitle,
                    n: resolved.songs.length
                });
            } else if (!startedIdle) {
                text = MessagesService.get(guildId, 'Musica:PuestaEnCola', songVars(first, guildId));
            } else {
                text = MessagesService.get(guildId, 'Musica:Tocando', songVars(first, guildId));
            }
            return { success: true, text, description: desc, musicWidget: 'send' };
        }
    },
    {
        name: 'music_skip',
        description: 'Skip the current song.',
        parameters: { type: 'object', properties: {} },
        destructive: false,
        async execute(ctx) {
            const fail = requireMusicControl(ctx, { description: '/skip' });
            if (fail) return fail;
            const next = skipCurrent(ctx.guild.id);
            const text = next
                ? MessagesService.get(ctx.guild.id, 'Musica:SaltadoProxima', songVars(next, ctx.guild.id))
                : MessagesService.get(ctx.guild.id, 'Musica:SaltadoVacio');
            return { success: true, text, description: '/skip', musicWidget: next ? 'refresh' : 'stop' };
        }
    },
    {
        name: 'music_pause',
        description: 'Pause the current song.',
        parameters: { type: 'object', properties: {} },
        destructive: false,
        async execute(ctx) {
            const fail = requireMusicControl(ctx, { description: '/pause' });
            if (fail) return fail;
            pausePlayback(ctx.guild.id);
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Musica:Pausado'),
                description: '/pause',
                musicWidget: 'refresh'
            };
        }
    },
    {
        name: 'music_resume',
        description: 'Resume the paused playback.',
        parameters: { type: 'object', properties: {} },
        destructive: false,
        async execute(ctx) {
            const fail = requireMusicControl(ctx, { description: '/resume' });
            if (fail) return fail;
            resumePlayback(ctx.guild.id);
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Musica:Reanudado'),
                description: '/resume',
                musicWidget: 'refresh'
            };
        }
    },
    {
        name: 'music_stop',
        description: 'Stop the music and disconnect the bot.',
        parameters: { type: 'object', properties: {} },
        destructive: false,
        async execute(ctx) {
            const access = canControlMusic(ctx);
            if (!access.ok) {
                return { success: false, text: access.message, description: '/stop' };
            }
            if (!getQueue(ctx.guild.id)) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'Musica:NoActivo'), description: '/stop' };
            }
            stopPlayback(ctx.guild.id);
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Musica:Detenido'),
                description: '/stop',
                musicWidget: 'stop'
            };
        }
    },
    {
        name: 'music_volume',
        description: 'Change the music volume. Accepts an absolute number (0-100), a relative adjustment like -10 or +5, or a simple expression like 30+20.',
        parameters: {
            type: 'object',
            properties: {
                level: { type: 'string', description: 'Volume: number (0-100), relative (-10, +5) or simple expression (30+20)' }
            },
            required: ['level']
        },
        destructive: false,
        async execute(ctx, args) {
            const result = applyVolumeInput(ctx.guild.id, args.level);
            if (!result.ok) {
                return {
                    success: false,
                    text: MessagesService.get(ctx.guild.id, 'Musica:VolumenInvalido'),
                    description: '/volume'
                };
            }
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Musica:Volumen', { nivel: result.level }),
                description: '/volume',
                musicWidget: getQueue(ctx.guild.id) ? 'refresh' : undefined
            };
        }
    },
    {
        name: 'music_shuffle',
        description: 'Shuffle the music queue (keeps the current song).',
        parameters: { type: 'object', properties: {} },
        destructive: false,
        async execute(ctx) {
            const fail = requireMusicControl(ctx, { description: '/shuffle' });
            if (fail) return fail;
            const ok = shuffleQueue(ctx.guild.id);
            return {
                success: ok,
                text: MessagesService.get(ctx.guild.id, ok ? 'Musica:Aleatorizado' : 'Musica:ColaVacia'),
                description: '/shuffle',
                musicWidget: ok ? 'refresh' : undefined
            };
        }
    },
    {
        name: 'music_seek',
        description: 'Jump to a specific position in the current song (e.g. 1:30 or 90).',
        parameters: {
            type: 'object',
            properties: {
                position: { type: 'string', description: 'Timestamp to jump to (e.g. 1:30 or 90)' }
            },
            required: ['position']
        },
        destructive: false,
        async execute(ctx, args) {
            const fail = requireMusicControl(ctx, { description: '/seek' });
            if (fail) return fail;
            const seconds = parseTimestamp(args.position);
            if (seconds == null) {
                return {
                    success: false,
                    text: MessagesService.get(ctx.guild.id, 'Musica:TimestampInvalido'),
                    description: '/seek'
                };
            }
            const result = seekTo(ctx.guild.id, seconds);
            if (!result.ok) {
                return {
                    success: false,
                    text: MessagesService.get(ctx.guild.id, 'Musica:ErrorSaltar'),
                    description: '/seek'
                };
            }
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Musica:SaltadoA', {
                    posicion: formatDuration(result.seconds, false, ctx.guild.id)
                }),
                description: '/seek',
                musicWidget: 'refresh'
            };
        }
    },
    {
        name: 'music_replay',
        description: 'Restart the current song from the beginning.',
        parameters: { type: 'object', properties: {} },
        destructive: false,
        async execute(ctx) {
            const fail = requireMusicControl(ctx, { description: '/replay' });
            if (fail) return fail;
            const result = replayCurrent(ctx.guild.id);
            if (!result.ok) {
                return {
                    success: false,
                    text: MessagesService.get(ctx.guild.id, 'Musica:ErrorSaltar'),
                    description: '/replay'
                };
            }
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Musica:Replay'),
                description: '/replay',
                musicWidget: 'refresh'
            };
        }
    },
    {
        name: 'music_loop',
        description: 'Loop the current track or the whole queue. Mode: off, track, or queue. Optional repeats (disable after N restarts) and duration (5m, 1h, 90).',
        parameters: {
            type: 'object',
            properties: {
                mode: { type: 'string', description: 'off, track, or queue' },
                repeats: { type: 'number', description: 'Disable the loop after this many restarts (e.g. 3)' },
                duration: { type: 'string', description: 'Disable after this long: 5m, 1h, 90, or 3:00' }
            },
            required: ['mode']
        },
        destructive: false,
        async execute(ctx, args) {
            const fail = requireMusicControl(ctx, { playing: false, description: '/loop' });
            if (fail) return fail;
            const mode = String(args.mode || '').toLowerCase();
            if (!['off', 'track', 'queue'].includes(mode)) {
                return {
                    success: false,
                    text: MessagesService.get(ctx.guild.id, 'Musica:LoopOff'),
                    description: '/loop'
                };
            }
            let repeats = args.repeats == null ? undefined : Math.round(Number(args.repeats));
            if (repeats != null && (!Number.isFinite(repeats) || repeats < 1 || repeats > 99)) {
                return {
                    success: false,
                    text: MessagesService.get(ctx.guild.id, 'Musica:LoopDuracionInvalida'),
                    description: '/loop'
                };
            }
            let durationMs;
            if (args.duration) {
                durationMs = parseLoopDuration(args.duration);
                if (durationMs == null || durationMs < 1000 || durationMs > 24 * 3_600_000) {
                    return {
                        success: false,
                        text: MessagesService.get(ctx.guild.id, 'Musica:LoopDuracionInvalida'),
                        description: '/loop'
                    };
                }
            }
            const result = setLoopMode(ctx.guild.id, mode, { repeats, durationMs });
            return {
                success: true,
                text: formatLoopReply(ctx.guild.id, result),
                description: '/loop',
                musicWidget: 'refresh'
            };
        }
    },
    {
        name: 'music_loop_ab',
        description: 'Loop a section of the current song. Actions: set-a, set-b, off. Optional timestamp; otherwise uses the current position.',
        parameters: {
            type: 'object',
            properties: {
                action: { type: 'string', description: 'set-a, set-b, or off' },
                position: { type: 'string', description: 'Optional timestamp (3:14 or 90)' }
            },
            required: ['action']
        },
        destructive: false,
        async execute(ctx, args) {
            const fail = requireMusicControl(ctx, { description: '/loop-ab' });
            if (fail) return fail;
            const action = String(args.action || '').toLowerCase();
            let position;
            if (args.position) {
                position = parseTimestamp(args.position);
                if (position == null) {
                    return {
                        success: false,
                        text: MessagesService.get(ctx.guild.id, 'Musica:TimestampInvalido'),
                        description: '/loop-ab'
                    };
                }
            }
            const result = setLoopAb(ctx.guild.id, action, position);
            if (!result.ok) {
                const key = result.reason === 'need-a' ? 'Musica:LoopABFaltaA' : 'Musica:ErrorSaltar';
                return { success: false, text: MessagesService.get(ctx.guild.id, key), description: '/loop-ab' };
            }
            let key = 'Musica:LoopABOff';
            const vars = {};
            if (result.action === 'set-a') {
                key = 'Musica:LoopABPuntoA';
                vars.posicion = formatDuration(result.position, false, ctx.guild.id);
            } else if (result.action === 'set-b') {
                key = 'Musica:LoopABPuntoB';
                vars.posicion = formatDuration(result.position, false, ctx.guild.id);
            }
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, key, vars),
                description: '/loop-ab',
                musicWidget: 'refresh'
            };
        }
    },
    {
        name: 'music_nightcore',
        description: 'Enable, disable, or toggle the nightcore audio profile (faster, higher pitch).',
        parameters: {
            type: 'object',
            properties: {
                state: { type: 'string', description: 'on, off, or toggle (default toggle)' }
            }
        },
        destructive: false,
        async execute(ctx, args) {
            const fail = requireMusicControl(ctx, { playing: false, description: '/nightcore' });
            if (fail) return fail;
            const state = String(args.state || 'toggle').toLowerCase();
            const pitch = setPitch(ctx.guild.id, state === 'on' ? 'nightcore' : state === 'off' ? 'off' : 'toggle-nightcore');
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, pitch === 'nightcore' ? 'Musica:NightcoreOn' : 'Musica:NightcoreOff'),
                description: '/nightcore',
                musicWidget: 'refresh'
            };
        }
    },
    {
        name: 'music_daycore',
        description: 'Enable, disable, or toggle the daycore audio profile (slower, lower pitch).',
        parameters: {
            type: 'object',
            properties: {
                state: { type: 'string', description: 'on, off, or toggle (default toggle)' }
            }
        },
        destructive: false,
        async execute(ctx, args) {
            const fail = requireMusicControl(ctx, { playing: false, description: '/daycore' });
            if (fail) return fail;
            const state = String(args.state || 'toggle').toLowerCase();
            const pitch = setPitch(ctx.guild.id, state === 'on' ? 'daycore' : state === 'off' ? 'off' : 'toggle-daycore');
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, pitch === 'daycore' ? 'Musica:DaycoreOn' : 'Musica:DaycoreOff'),
                description: '/daycore',
                musicWidget: 'refresh'
            };
        }
    },
    {
        name: 'music_bassboost',
        description: 'Set bass boost intensity from 0 (off) to 100.',
        parameters: {
            type: 'object',
            properties: {
                intensity: { type: 'number', description: 'Bass boost 0-100 (0 disables)' }
            },
            required: ['intensity']
        },
        destructive: false,
        async execute(ctx, args) {
            const fail = requireMusicControl(ctx, { playing: false, description: '/bassboost' });
            if (fail) return fail;
            const level = setBass(ctx.guild.id, args.intensity);
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Musica:BassBoost', { nivel: level }),
                description: '/bassboost',
                musicWidget: 'refresh'
            };
        }
    },

    // ── AFK ──
    {
        name: 'afk_set',
        description: 'Set the requesting user\'s AFK status with an optional reason.',
        parameters: {
            type: 'object',
            properties: {
                reason: { type: 'string', description: 'Optional reason for being AFK' }
            }
        },
        destructive: false,
        async execute(ctx, args) {
            const reason = await setAfk(ctx.member, args.reason);
            return {
                success: true,
                text: `💤 ${MessagesService.get(ctx.guild.id, 'Afk:Establecido', { motivo: reason })}`,
                description: '/afk set'
            };
        }
    },
    {
        name: 'afk_remove',
        description: 'Remove AFK status for yourself, or for another member if you can manage the server.',
        parameters: {
            type: 'object',
            properties: {
                user: { type: 'string', description: 'Optional member to clear AFK for (moderators only)' }
            }
        },
        destructive: false,
        async execute(ctx, args) {
            let target = ctx.member;
            if (args.user) {
                const resolved = resolveMember(ctx.guild, args.user);
                if (resolved && resolved.id !== ctx.member.id) {
                    if (!ctx.member.permissions.has(PermissionFlagsBits.ManageGuild)) {
                        return {
                            success: false,
                            text: MessagesService.get(ctx.guild.id, 'Errores:SinPermisos'),
                            description: '/afk remove'
                        };
                    }
                    target = resolved;
                }
            }
            const removed = await removeAfk(target);
            const desc = `/afk remove`;
            if (removed) {
                return {
                    success: true,
                    text: MessagesService.get(ctx.guild.id, 'Afk:RemovidoMod', { usuario: target.displayName }),
                    description: desc
                };
            }
            return {
                success: false,
                text: MessagesService.get(ctx.guild.id, 'Afk:NoEstaAusente', { usuario: target.displayName }),
                description: desc
            };
        }
    },
    {
        name: 'afk_list',
        description: 'List members currently marked as AFK in this server.',
        parameters: { type: 'object', properties: {} },
        destructive: false,
        async execute(ctx) {
            const ausentes = listAfk(ctx.guild.id);
            if (!ausentes.length) {
                return {
                    success: true,
                    text: MessagesService.get(ctx.guild.id, 'Afk:SinMiembrosAusentes'),
                    description: '/afk list'
                };
            }
            const text = ausentes.map(a =>
                `• <@${a.userId}> — *"${a.reason}"* (<t:${Math.floor(a.timestamp / 1000)}:R>)`
            ).join('\n');
            return { success: true, text, description: '/afk list' };
        }
    },

    // ── ROLES ──
    {
        name: 'role_add',
        description: 'Add a role to a server member. Optional duration (1m, 12h, 4d) to remove it later. Requires Manage Roles.',
        parameters: {
            type: 'object',
            properties: {
                user: { type: 'string', description: 'Member: mention, ID or username' },
                role: { type: 'string', description: 'Role name, mention or ID' },
                duration: { type: 'string', description: 'Optional how long to keep the role: 1m, 12h, 4d' }
            },
            required: ['user', 'role']
        },
        destructive: false,
        requiredPermissions: [PermissionFlagsBits.ManageRoles],
        async execute(ctx, args) {
            return executeRoleTool(ctx, args, true);
        }
    },
    {
        name: 'role_remove',
        description: 'Remove a role from a server member. Requires Manage Roles.',
        parameters: {
            type: 'object',
            properties: {
                user: { type: 'string', description: 'Member: mention, ID or username' },
                role: { type: 'string', description: 'Role name, mention or ID' }
            },
            required: ['user', 'role']
        },
        destructive: false,
        requiredPermissions: [PermissionFlagsBits.ManageRoles],
        async execute(ctx, args) {
            return executeRoleTool(ctx, args, false);
        }
    },

    {
        name: 'lock_channel',
        description: 'Lock a channel so nobody can talk (or connect, if voice). Requires Manage Channels.',
        parameters: {
            type: 'object',
            properties: {
                channel: { type: 'string', description: 'Channel mention/ID/name, or "current"' },
                reason: { type: 'string', description: 'Optional reason' }
            }
        },
        destructive: false,
        async execute(ctx, args) {
            const desc = '/lock';
            const channel = channelOf(ctx, args.channel);
            const reason = args.reason || defaultReason(ctx.guild.id);
            if (!isLockableChannel(channel)) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'Bloqueo:CanalInvalido'), description: desc };
            }
            if (!channel.permissionsFor(ctx.member)?.has(PermissionFlagsBits.ManageChannels)) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'Bloqueo:SinPermisosCanal', { canal: channel.toString() }), description: desc };
            }
            const applied = await lockChannel(channel, reason);
            return {
                success: applied,
                text: MessagesService.get(ctx.guild.id, applied ? 'Bloqueo:Bloqueado' : 'Bloqueo:YaBloqueado', { canal: channel.toString() }),
                description: desc
            };
        }
    },
    {
        name: 'unlock_channel',
        description: 'Unlock a channel previously locked with /lock.',
        parameters: {
            type: 'object',
            properties: {
                channel: { type: 'string', description: 'Channel mention/ID/name, or "current"' },
                reason: { type: 'string', description: 'Optional reason' }
            }
        },
        destructive: false,
        async execute(ctx, args) {
            const desc = '/unlock';
            const channel = channelOf(ctx, args.channel);
            const reason = args.reason || defaultReason(ctx.guild.id);
            if (!isLockableChannel(channel)) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'Bloqueo:CanalInvalido'), description: desc };
            }
            if (!channel.permissionsFor(ctx.member)?.has(PermissionFlagsBits.ManageChannels)) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'Bloqueo:SinPermisosCanal', { canal: channel.toString() }), description: desc };
            }
            const applied = await unlockChannel(channel, reason);
            return {
                success: applied,
                text: MessagesService.get(ctx.guild.id, applied ? 'Bloqueo:Desbloqueado' : 'Bloqueo:NoBloqueado', { canal: channel.toString() }),
                description: desc
            };
        }
    },
    {
        name: 'untimeout_user',
        description: "Remove a member's timeout. Requires Moderate Members.",
        parameters: {
            type: 'object',
            properties: {
                user: { type: 'string', description: 'Member mention, ID or username' },
                reason: { type: 'string', description: 'Optional reason' }
            },
            required: ['user']
        },
        destructive: false,
        async execute(ctx, args) {
            const desc = '/untimeout';
            const perm = failPerm(ctx, PermissionFlagsBits.ModerateMembers, desc);
            if (perm) return perm;
            const member = await resolveMemberAsync(ctx.guild, args.user);
            if (!member) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'Moderacion:Errores:NoEnServidor', { usuario: args.user }), description: desc };
            }
            const hier = failHierarchy(ctx, member, desc);
            if (hier) return hier;
            const reason = args.reason || defaultReason(ctx.guild.id);
            await member.timeout(null, reason);
            const incident = registerIncident(ctx.guild.id, member.user, ctx.member.user, IncidentType.FinAislamiento, reason);
            await announceIncident(ctx.guild, incident);
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Moderacion:Exito:FinAislamiento', { usuario: member.user.username }),
                description: desc
            };
        }
    },
    {
        name: 'get_user_history',
        description: "Show a member's recent moderation incidents (read-only). Requires Moderate Members.",
        parameters: {
            type: 'object',
            properties: {
                user: { type: 'string', description: 'Optional member; empty = latest server incidents' }
            }
        },
        destructive: false,
        async execute(ctx, args) {
            const desc = '/history';
            const perm = failPerm(ctx, PermissionFlagsBits.ModerateMembers, desc);
            if (perm) return perm;
            const member = args.user ? await resolveMemberAsync(ctx.guild, args.user) : null;
            const rows = member
                ? db.prepare('SELECT Id, CAST(TargetUserId AS TEXT) as TargetUserId, Type, Reason, Duration FROM Incidents WHERE GuildId = ? AND TargetUserId = ? ORDER BY Id DESC LIMIT 10').all(ctx.guild.id, member.id)
                : db.prepare('SELECT Id, CAST(TargetUserId AS TEXT) as TargetUserId, Type, Reason, Duration FROM Incidents WHERE GuildId = ? ORDER BY Id DESC LIMIT 10').all(ctx.guild.id);
            if (!rows.length) {
                return { success: true, text: MessagesService.get(ctx.guild.id, 'Moderacion:Historial:Vacio'), description: desc };
            }
            const text = rows.map(row => {
                const typeLabel = MessagesService.get(ctx.guild.id, `Moderacion:Tipos:${row.Type}`);
                const extra = row.Duration ? ` · ${row.Duration}` : '';
                return `#${row.Id} ${typeLabel}${extra} — <@${row.TargetUserId}>: ${row.Reason || '—'}`;
            }).join('\n');
            return { success: true, text, description: desc };
        }
    },
    {
        name: 'softban_user',
        description: 'Ban and immediately unban a member to delete their messages. Destructive; requires Ban Members.',
        parameters: {
            type: 'object',
            properties: {
                user: { type: 'string', description: 'Member mention, ID or username' },
                reason: { type: 'string', description: 'Optional reason' }
            },
            required: ['user']
        },
        destructive: true,
        describe: async (ctx, args) => `/softban ${args.user}`,
        async execute(ctx, args) {
            const desc = '/softban';
            const perm = failPerm(ctx, PermissionFlagsBits.BanMembers, desc);
            if (perm) return perm;
            const member = await resolveMemberAsync(ctx.guild, args.user);
            const user = member?.user || ctx.guild.client.users.cache.get(String(args.user || '').replace(/\D/g, ''));
            if (!user && !member) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'Moderacion:Errores:NoEnServidor', { usuario: args.user }), description: desc };
            }
            if (member) {
                const hier = failHierarchy(ctx, member, desc);
                if (hier) return hier;
            }
            const reason = args.reason || defaultReason(ctx.guild.id);
            const targetUser = member?.user || user;
            if (member) await notifyMemberDm(member, 'Softban', reason);
            await ctx.guild.members.ban(targetUser.id, { deleteMessageSeconds: 7 * 24 * 60 * 60, reason: `Softban: ${reason}` });
            await ctx.guild.members.unban(targetUser.id, 'Softban: automatic unban');
            const incident = registerIncident(ctx.guild.id, targetUser, ctx.member.user, IncidentType.Softban, reason);
            await announceIncident(ctx.guild, incident);
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Moderacion:Exito:Softban', { usuario: targetUser.username }),
                description: desc
            };
        }
    },
    {
        name: 'hardmute_user',
        description: 'Strip roles and revoke send/speak in all channels. Optional duration (30m, 2h). Destructive; requires Manage Roles.',
        parameters: {
            type: 'object',
            properties: {
                user: { type: 'string', description: 'Member mention, ID or username' },
                duration: { type: 'string', description: 'Optional duration: 30m, 2h, 7d' },
                reason: { type: 'string', description: 'Optional reason' }
            },
            required: ['user']
        },
        destructive: true,
        describe: async (ctx, args) => `/hardmute ${args.user} ${args.duration || ''}`.trim(),
        async execute(ctx, args) {
            const desc = '/hardmute';
            const perm = failPerm(ctx, PermissionFlagsBits.ManageRoles, desc);
            if (perm) return perm;
            const member = await resolveMemberAsync(ctx.guild, args.user);
            if (!member) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'Moderacion:Errores:NoEnServidor', { usuario: args.user }), description: desc };
            }
            const hier = failHierarchy(ctx, member, desc);
            if (hier) return hier;
            const reason = args.reason || defaultReason(ctx.guild.id);
            let expiresAtIso = null;
            if (args.duration) {
                const ms = parseDuration(String(args.duration));
                if (!ms || ms <= 0) {
                    return { success: false, text: MessagesService.get(ctx.guild.id, 'Moderacion:Errores:DuracionInvalida'), description: desc };
                }
                expiresAtIso = new Date(Date.now() + ms).toISOString();
            }
            await performHardmute(ctx.guild, member, { reason, moderator: ctx.member.user, expiresAtIso });
            if (expiresAtIso) scheduleUnhardmute(ctx.client, ctx.guild.id, member.id, expiresAtIso);
            await notifyMemberDm(member, 'Hardmute', reason);
            const incident = registerIncident(ctx.guild.id, member.user, ctx.member.user, IncidentType.Hardmute, reason, args.duration || null);
            await announceIncident(ctx.guild, incident);
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Moderacion:Exito:Hardmute', { usuario: member.user.username }),
                description: desc
            };
        }
    },
    {
        name: 'unhardmute_user',
        description: 'Restore roles and permissions after a hardmute. Requires Manage Roles.',
        parameters: {
            type: 'object',
            properties: {
                user: { type: 'string', description: 'Member mention, ID or username' },
                reason: { type: 'string', description: 'Optional reason' }
            },
            required: ['user']
        },
        destructive: false,
        async execute(ctx, args) {
            const desc = '/unhardmute';
            const perm = failPerm(ctx, PermissionFlagsBits.ManageRoles, desc);
            if (perm) return perm;
            const member = await resolveMemberAsync(ctx.guild, args.user);
            if (!member) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'Moderacion:Errores:NoEnServidor', { usuario: args.user }), description: desc };
            }
            const reason = args.reason || defaultReason(ctx.guild.id);
            await performUnhardmute(ctx.guild, member.id, reason, ctx.member.user);
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Moderacion:Exito:FinHardmute', { usuario: member.user.username }),
                description: desc
            };
        }
    },
    {
        name: 'welcome_set_channel',
        description: 'Set the channel where new members are welcomed. Requires Manage Server.',
        parameters: {
            type: 'object',
            properties: {
                channel: { type: 'string', description: 'Text channel mention/ID/name, or "current"' }
            },
            required: ['channel']
        },
        destructive: false,
        async execute(ctx, args) {
            const desc = '/welcome channel';
            const perm = failPerm(ctx, PermissionFlagsBits.ManageGuild, desc);
            if (perm) return perm;
            const channel = channelOf(ctx, args.channel);
            if (!channel || channel.type !== ChannelType.GuildText) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'Bienvenida:VerNoConfigurado'), description: desc };
            }
            updateGuildConfig(ctx.guild.id, { WelcomeChannelId: channel.id });
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Bienvenida:ConfigCanalExito', { canal: channel.toString() }),
                description: desc
            };
        }
    },
    {
        name: 'welcome_set_message',
        description: 'Set the welcome message ({usuario} and {servidor} placeholders). Requires Manage Server.',
        parameters: {
            type: 'object',
            properties: {
                message: { type: 'string', description: 'Welcome text, max 1900 characters' }
            },
            required: ['message']
        },
        destructive: false,
        async execute(ctx, args) {
            const desc = '/welcome message';
            const perm = failPerm(ctx, PermissionFlagsBits.ManageGuild, desc);
            if (perm) return perm;
            const message = String(args.message || '');
            if (!message.trim() || message.length > 1900) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'Bienvenida:MensajeLargo'), description: desc };
            }
            updateGuildConfig(ctx.guild.id, { WelcomeMessage: message });
            const preview = message
                .replace(/{usuario}|{user}/g, ctx.member.toString())
                .replace(/{servidor}|{server}/g, ctx.guild.name);
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Bienvenida:ConfigMensajeExito', { vista: preview }),
                description: desc
            };
        }
    },
    {
        name: 'welcome_disable',
        description: 'Disable welcome messages. Requires Manage Server.',
        parameters: { type: 'object', properties: {} },
        destructive: false,
        async execute(ctx) {
            const desc = '/welcome disable';
            const perm = failPerm(ctx, PermissionFlagsBits.ManageGuild, desc);
            if (perm) return perm;
            const row = db.prepare('SELECT WelcomeChannelId FROM GuildConfigs WHERE GuildId = ?').get(ctx.guild.id);
            if (!row?.WelcomeChannelId) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'Bienvenida:YaDesactivada'), description: desc };
            }
            updateGuildConfig(ctx.guild.id, { WelcomeChannelId: null });
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Bienvenida:ConfigDesactivada'),
                description: desc
            };
        }
    },
    {
        name: 'counting_set_channel',
        description: 'Set the text channel for the counting game. Requires Manage Server.',
        parameters: {
            type: 'object',
            properties: {
                channel: { type: 'string', description: 'Text channel mention/ID/name, or "current"' }
            },
            required: ['channel']
        },
        destructive: false,
        async execute(ctx, args) {
            const desc = '/counting channel';
            const perm = failPerm(ctx, PermissionFlagsBits.ManageGuild, desc);
            if (perm) return perm;
            const channel = channelOf(ctx, args.channel);
            if (!channel || channel.type !== ChannelType.GuildText) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'Conteo:CanalDebeSerTexto'), description: desc };
            }
            ensureCountingRow(ctx.guild.id);
            db.prepare('UPDATE CountingConfigs SET ChannelId = ? WHERE GuildId = ?').run(channel.id, ctx.guild.id);
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Conteo:CanalEstablecido', { canal: channel.toString() }),
                description: desc
            };
        }
    },
    {
        name: 'counting_disable',
        description: 'Disable the counting game. Requires Manage Server.',
        parameters: { type: 'object', properties: {} },
        destructive: false,
        async execute(ctx) {
            const desc = '/counting disable';
            const perm = failPerm(ctx, PermissionFlagsBits.ManageGuild, desc);
            if (perm) return perm;
            ensureCountingRow(ctx.guild.id);
            const cfg = db.prepare('SELECT ChannelId FROM CountingConfigs WHERE GuildId = ?').get(ctx.guild.id);
            if (!cfg?.ChannelId) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'Conteo:YaDesactivado'), description: desc };
            }
            db.prepare('UPDATE CountingConfigs SET ChannelId = NULL WHERE GuildId = ?').run(ctx.guild.id);
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Conteo:Desactivado'),
                description: desc
            };
        }
    },
    {
        name: 'counting_set_goal',
        description: 'Set a numeric goal for the counting game. Requires Manage Server.',
        parameters: {
            type: 'object',
            properties: {
                number: { type: 'number', description: 'Goal number greater than 0' }
            },
            required: ['number']
        },
        destructive: false,
        async execute(ctx, args) {
            const desc = '/counting goal';
            const perm = failPerm(ctx, PermissionFlagsBits.ManageGuild, desc);
            if (perm) return perm;
            const number = Math.round(Number(args.number));
            if (!Number.isFinite(number) || number <= 0) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'Conteo:ObjetivoInvalido'), description: desc };
            }
            ensureCountingRow(ctx.guild.id);
            db.prepare('UPDATE CountingConfigs SET Goal = ? WHERE GuildId = ?').run(number, ctx.guild.id);
            const cfg = db.prepare('SELECT Base FROM CountingConfigs WHERE GuildId = ?').get(ctx.guild.id);
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Conteo:ObjetivoEstablecido', {
                    objetivo: formatNumber(number, cfg?.Base || 'Decimal')
                }),
                description: desc
            };
        }
    },
    {
        name: 'counting_remove_goal',
        description: 'Remove the counting game goal. Requires Manage Server.',
        parameters: { type: 'object', properties: {} },
        destructive: false,
        async execute(ctx) {
            const desc = '/counting goal-remove';
            const perm = failPerm(ctx, PermissionFlagsBits.ManageGuild, desc);
            if (perm) return perm;
            ensureCountingRow(ctx.guild.id);
            db.prepare('UPDATE CountingConfigs SET Goal = NULL WHERE GuildId = ?').run(ctx.guild.id);
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Conteo:ObjetivoQuitado'),
                description: desc
            };
        }
    },
    {
        name: 'youtube_follow',
        description: 'Subscribe to a YouTube channel and announce new videos in a Discord channel. Requires Manage Server.',
        parameters: {
            type: 'object',
            properties: {
                channel: { type: 'string', description: 'YouTube URL or @handle' },
                notify: { type: 'string', description: 'Discord text channel: mention/ID/name or "current"' }
            },
            required: ['channel', 'notify']
        },
        destructive: false,
        async execute(ctx, args) {
            const desc = '/youtube follow';
            const perm = failPerm(ctx, PermissionFlagsBits.ManageGuild, desc);
            if (perm) return perm;
            const notify = channelOf(ctx, args.notify);
            if (!notify || notify.type !== ChannelType.GuildText) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'Conteo:CanalDebeSerTexto'), description: desc };
            }
            const resolved = await resolveYouTubeChannel(args.channel);
            if (!resolved) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'YouTube:ErrorResolver'), description: desc };
            }
            const existing = db.prepare('SELECT YTChannelId FROM YouTubeSubscriptions WHERE GuildId = ?').get(ctx.guild.id);
            const latest = await getLatestVideo(resolved.channelId);
            db.prepare(`
                INSERT INTO YouTubeSubscriptions (GuildId, YTChannelId, YTChannelName, NotifyChannelId, NotifyRoleId, LastVideoId, CustomMessage, CreatedAt)
                VALUES (?, ?, ?, ?, NULL, ?, NULL, ?)
                ON CONFLICT(GuildId) DO UPDATE SET
                    YTChannelId = excluded.YTChannelId,
                    YTChannelName = excluded.YTChannelName,
                    NotifyChannelId = excluded.NotifyChannelId,
                    LastVideoId = excluded.LastVideoId
            `).run(ctx.guild.id, resolved.channelId, resolved.channelName, notify.id, latest?.videoId || null, new Date().toISOString());
            const key = existing ? 'YouTube:SeguirReemplazado' : 'YouTube:SeguirExito';
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, key, { canal: resolved.channelName, destino: notify.toString() }),
                description: desc
            };
        }
    },
    {
        name: 'youtube_unfollow',
        description: "Remove the server's YouTube subscription. Requires Manage Server.",
        parameters: { type: 'object', properties: {} },
        destructive: false,
        async execute(ctx) {
            const desc = '/youtube unfollow';
            const perm = failPerm(ctx, PermissionFlagsBits.ManageGuild, desc);
            if (perm) return perm;
            const res = db.prepare('DELETE FROM YouTubeSubscriptions WHERE GuildId = ?').run(ctx.guild.id);
            return {
                success: res.changes > 0,
                text: MessagesService.get(ctx.guild.id, res.changes > 0 ? 'YouTube:Dejado' : 'YouTube:NoSuscrito'),
                description: desc
            };
        }
    },
    {
        name: 'colors_install',
        description: 'Install a name-color palette (normal or pastel). Requires Manage Roles.',
        parameters: {
            type: 'object',
            properties: {
                palette: { type: 'string', description: '"normal" or "pastel"' }
            }
        },
        destructive: false,
        async execute(ctx, args) {
            const desc = '/colors install';
            const perm = failPerm(ctx, PermissionFlagsBits.ManageRoles, desc);
            if (perm) return perm;
            const palette = String(args.palette || 'normal').toLowerCase() === 'pastel' ? 'pastel' : 'normal';
            const { created, removed, total } = await installPalette(ctx.guild, palette);
            const key = created === 0 && removed === 0 ? 'Colores:InstalarRepetido' : 'Colores:Instalar';
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, key, { paleta: palette, total }),
                description: desc
            };
        }
    },
    {
        name: 'colors_uninstall',
        description: 'Remove the server color palette. Requires Manage Roles.',
        parameters: { type: 'object', properties: {} },
        destructive: false,
        async execute(ctx) {
            const desc = '/colors uninstall';
            const perm = failPerm(ctx, PermissionFlagsBits.ManageRoles, desc);
            if (perm) return perm;
            const deleted = await uninstallPalette(ctx.guild);
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Colores:Desinstalar', { borrados: deleted }),
                description: desc
            };
        }
    },
    {
        name: 'channel_create',
        description: 'Create a text or voice channel. Requires Manage Channels.',
        parameters: {
            type: 'object',
            properties: {
                name: { type: 'string', description: 'Channel name' },
                type: { type: 'string', description: '"voice" or "text"' },
                category: { type: 'string', description: 'Optional category mention/ID/name' }
            },
            required: ['name', 'type']
        },
        destructive: false,
        async execute(ctx, args) {
            const desc = '/channel create';
            const perm = failPerm(ctx, PermissionFlagsBits.ManageChannels, desc);
            if (perm) return perm;
            const name = String(args.name || '').trim();
            if (!name || name.length > 100) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'Chat:Error'), description: desc };
            }
            const kind = String(args.type || 'text').toLowerCase() === 'voice' ? ChannelType.GuildVoice : ChannelType.GuildText;
            const category = args.category ? resolveChannel(ctx.guild, args.category) : null;
            const created = await ctx.guild.channels.create({
                name,
                type: kind,
                parent: category?.id,
                reason: 'Created from AI chat'
            });
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Voces:Creado', { canal: created.toString() }),
                description: desc
            };
        }
    },
    {
        name: 'logchannel_set',
        description: 'Set the channel where moderation incidents are announced. Requires Manage Server.',
        parameters: {
            type: 'object',
            properties: {
                channel: { type: 'string', description: 'Text channel mention/ID/name, or "current"' }
            },
            required: ['channel']
        },
        destructive: false,
        async execute(ctx, args) {
            const desc = '/modlog';
            const perm = failPerm(ctx, PermissionFlagsBits.ManageGuild, desc);
            if (perm) return perm;
            const channel = channelOf(ctx, args.channel);
            if (!channel || channel.type !== ChannelType.GuildText) {
                return { success: false, text: MessagesService.get(ctx.guild.id, 'Config:VerNoConfigurado'), description: desc };
            }
            updateGuildConfig(ctx.guild.id, { ModLogChannelId: channel.id });
            return {
                success: true,
                text: MessagesService.get(ctx.guild.id, 'Config:CanalLogsEstablecido', { canal: channel.toString() }),
                description: desc
            };
        }
    },
];

async function executeRoleTool(ctx, args, add) {
    const desc = add ? '/role add' : '/role remove';
    if (!ctx.member.permissions.has(PermissionFlagsBits.ManageRoles)) {
        return { success: false, text: MessagesService.get(ctx.guild.id, 'Errores:SinPermisos'), description: desc };
    }
    const member = resolveMember(ctx.guild, args.user);
    if (!member) {
        return {
            success: false,
            text: MessagesService.get(ctx.guild.id, 'Moderacion:Errores:NoEnServidor', { usuario: args.user }),
            description: desc
        };
    }
    const role = resolveRole(ctx.guild, args.role);
    if (!role) {
        return { success: false, text: MessagesService.get(ctx.guild.id, 'Roles:NoEncontrado'), description: desc };
    }
    let durationMs = null;
    if (add && args.duration) {
        durationMs = parseDuration(String(args.duration));
        if (!durationMs || durationMs < 1000 || durationMs > MAX_TEMP_ROLE_MS) {
            return {
                success: false,
                text: MessagesService.get(ctx.guild.id, 'Roles:DuracionInvalida'),
                description: desc
            };
        }
    }

    const result = await changeMemberRole({
        guild: ctx.guild,
        actor: ctx.member,
        member,
        role,
        add,
        reason: `${add ? 'Added' : 'Removed'} via AI for ${ctx.member.user.username} (${ctx.member.id})`
    });
    if (!result.ok) {
        return {
            success: false,
            text: MessagesService.get(ctx.guild.id, result.key, result.vars),
            description: desc
        };
    }
    if (add && durationMs && (result.key === 'Roles:Asignado' || result.key === 'Roles:YaTiene')) {
        scheduleTimedRole(ctx.guild.id, member.id, role.id, durationMs);
        return {
            success: true,
            text: MessagesService.get(ctx.guild.id, 'Roles:AsignadoTemporal', {
                ...result.vars,
                duracion: formatCompactDuration(durationMs)
            }),
            description: desc
        };
    }
    if (!add && result.key === 'Roles:Removido') {
        cancelTimedRole(ctx.guild.id, member.id, role.id);
    }
    return {
        success: true,
        text: MessagesService.get(ctx.guild.id, result.key, result.vars),
        description: desc
    };
}

function uniqueTools() {
    const seen = new Set();
    return tools.filter(tool => {
        if (seen.has(tool.name)) {
            console.warn(`[aiTools] duplicate tool skipped: ${tool.name}`);
            return false;
        }
        seen.add(tool.name);
        return true;
    });
}

export function getToolsForDeepSeek() {
    return uniqueTools().map(t => ({
        type: 'function',
        name: t.name,
        description: t.description,
        parameters: t.parameters
    }));
}

export function getToolsForGemini() {
    return uniqueTools().map(t => ({
        name: t.name,
        description: t.description,
        parameters: t.parameters
    }));
}

export function getToolByName(name) {
    return tools.find(t => t.name === name);
}
