import db from './database.js';
import { PermissionFlagsBits } from 'discord.js';

/**
 * All tools available to the AI model.
 * Each tool has:
 *   - name: string
 *   - description: string
 *   - parameters: JSON Schema object
 *   - destructive: boolean (requires user confirmation with buttons before execution)
 *   - requiredPermissions: Array of PermissionFlagsBits
 *   - describe(ctx, args): async (ctx, args) => string (User-facing description for the confirmation)
 *   - execute(ctx, args): async (ctx, args) => { success, text, description }
 */

// ──────────────────────────────────────────────
// Helpers
// ──────────────────────────────────────────────

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

export function parseDuration(str) {
    const match = str?.match(/^(\d+)(s|m|h|d)$/i);
    if (!match) return null;
    const n = parseInt(match[1]);
    const unit = match[2].toLowerCase();
    const multipliers = { s: 1000, m: 60000, h: 3600000, d: 86400000 };
    return n * multipliers[unit];
}

function recordIncident(guildId, targetMember, moderatorMember, type, reason, duration = null) {
    try {
        db.prepare(`
            INSERT INTO Incidents (GuildId, TargetUserId, TargetTag, ModeratorId, ModeratorTag, Type, Reason, Duration, CreatedAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
        `).run(
            guildId,
            targetMember.id,
            targetMember.user.tag || targetMember.user.username,
            moderatorMember.id,
            moderatorMember.user.tag || moderatorMember.user.username,
            type,
            reason || 'Sin motivo especificado',
            duration ? String(duration) : null,
            new Date().toISOString()
        );
    } catch (e) {
        console.error('[aiTools] Error recording incident:', e);
    }
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
            const state = {
                guildName: ctx.guild.name,
                language: row?.Language || 'en',
                aiChatEnabled: row?.AiChatEnabled !== 0,
                aiMentionsEnabled: row?.AiMentionsEnabled === 1,
                aiWebSearchEnabled: row?.AiWebSearchEnabled !== 0,
                aiCommandsEnabled: row?.AiCommandsEnabled !== 0,
                volume: row?.Volume ?? 100,
                welcomeChannelId: row?.WelcomeChannelId?.toString() || null,
                welcomeMessage: row?.WelcomeMessage || null,
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

            recordIncident(ctx.guild.id, member, ctx.member, 'Advertencia', args.reason);

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
            recordIncident(ctx.guild.id, member, ctx.member, 'Silencio', args.reason, args.duration);

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
            recordIncident(ctx.guild.id, member, ctx.member, 'Expulsion', args.reason);

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
            recordIncident(ctx.guild.id, member, ctx.member, 'Veto', args.reason);

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
    {
        name: 'role_add',
        description: 'Add a role to a member.',
        parameters: {
            type: 'object',
            properties: {
                user: { type: 'string', description: 'User mention, ID or username' },
                role: { type: 'string', description: 'Role mention, ID or name' }
            },
            required: ['user', 'role']
        },
        destructive: false,
        async execute(ctx, args) {
            if (!ctx.member.permissions.has(PermissionFlagsBits.ManageRoles)) {
                return { success: false, text: 'No tienes permisos para asignar roles.', description: 'Asignar rol' };
            }

            const member = resolveMember(ctx.guild, args.user);
            const role = resolveRole(ctx.guild, args.role);
            if (!member) return { success: false, text: `No encontré al usuario: ${args.user}`, description: 'Asignar rol' };
            if (!role) return { success: false, text: `No encontré el rol: ${args.role}`, description: 'Asignar rol' };

            await member.roles.add(role);
            return {
                success: true,
                text: `✅ Rol **${role.name}** asignado a **${member.displayName}**.`,
                description: `Asignar rol ${role.name}`
            };
        }
    },
    {
        name: 'role_remove',
        description: 'Remove a role from a member.',
        parameters: {
            type: 'object',
            properties: {
                user: { type: 'string', description: 'User mention, ID or username' },
                role: { type: 'string', description: 'Role mention, ID or name' }
            },
            required: ['user', 'role']
        },
        destructive: false,
        async execute(ctx, args) {
            if (!ctx.member.permissions.has(PermissionFlagsBits.ManageRoles)) {
                return { success: false, text: 'No tienes permisos para remover roles.', description: 'Remover rol' };
            }

            const member = resolveMember(ctx.guild, args.user);
            const role = resolveRole(ctx.guild, args.role);
            if (!member) return { success: false, text: `No encontré al usuario: ${args.user}`, description: 'Remover rol' };
            if (!role) return { success: false, text: `No encontré el rol: ${args.role}`, description: 'Remover rol' };

            await member.roles.remove(role);
            return {
                success: true,
                text: `✅ Rol **${role.name}** removido de **${member.displayName}**.`,
                description: `Remover rol ${role.name}`
            };
        }
    },

    // ── MUSIC ──
    {
        name: 'music_volume',
        description: 'Set music volume (0-100) or adjust relatively (+10, -10).',
        parameters: {
            type: 'object',
            properties: {
                level: { type: 'string', description: 'Volume level 0-100 or relative like +10 or -10' }
            },
            required: ['level']
        },
        destructive: false,
        async execute(ctx, args) {
            const row = db.prepare('SELECT Volume FROM GuildConfigs WHERE GuildId = ?').get(ctx.guild.id);
            const current = row?.Volume ?? 100;
            let newVolume;

            if (args.level.startsWith('+')) {
                newVolume = Math.min(100, current + parseInt(args.level.slice(1)));
            } else if (args.level.startsWith('-')) {
                newVolume = Math.max(0, current - parseInt(args.level.slice(1)));
            } else {
                newVolume = Math.min(100, Math.max(0, parseInt(args.level) || current));
            }

            db.prepare('INSERT OR IGNORE INTO GuildConfigs (GuildId) VALUES (?)').run(ctx.guild.id);
            db.prepare('UPDATE GuildConfigs SET Volume = ? WHERE GuildId = ?').run(newVolume, ctx.guild.id);

            return {
                success: true,
                text: `🔊 Volumen ajustado al **${newVolume}%**.`,
                description: `Ajustar volumen a ${newVolume}%`
            };
        }
    },

    // ── AFK ──
    {
        name: 'afk_set',
        description: 'Set the AFK status for a user.',
        parameters: {
            type: 'object',
            properties: {
                user: { type: 'string', description: 'User mention, ID or username (defaults to caller)' },
                reason: { type: 'string', description: 'AFK reason' }
            }
        },
        destructive: false,
        async execute(ctx, args) {
            const member = args.user ? resolveMember(ctx.guild, args.user) : ctx.member;
            if (!member) return { success: false, text: 'Usuario no encontrado.', description: 'Establecer AFK' };

            db.prepare(`
                INSERT INTO AfkUsers (GuildId, UserId, Reason, SetAt, OriginalNickname)
                VALUES (?, ?, ?, ?, ?)
                ON CONFLICT(GuildId, UserId) DO UPDATE SET Reason = excluded.Reason, SetAt = excluded.SetAt
            `).run(ctx.guild.id, member.id, args.reason || 'AFK', new Date().toISOString(), member.nickname || null);

            return {
                success: true,
                text: `💤 **${member.displayName}** ahora está AFK: ${args.reason || 'AFK'}`,
                description: `AFK para @${member.displayName}`
            };
        }
    },
];

export function getToolsForDeepSeek() {
    return tools.map(t => ({
        type: 'function',
        name: t.name,
        description: t.description,
        parameters: t.parameters
    }));
}

export function getToolsForGemini() {
    return tools.map(t => ({
        name: t.name,
        description: t.description,
        parameters: t.parameters
    }));
}

export function getToolByName(name) {
    return tools.find(t => t.name === name);
}
