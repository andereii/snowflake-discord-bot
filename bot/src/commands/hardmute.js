import { SlashCommandBuilder, PermissionFlagsBits, ChannelType } from 'discord.js';
import { registerIncident, announceIncident, notifyMemberDm, IncidentType } from '../services/moderationLog.js';
import { scheduleUnhardmute } from '../services/hardmuteManager.js';
import { parseDuration } from '../services/aiTools.js';
import MessagesService from '../services/messagesService.js';
import db from '../services/database.js';

export const data = new SlashCommandBuilder()
    .setName('hardmute')
    .setDescription('Strip roles and revoke send/speak permissions in all channels')
    .addUserOption(option => 
        option.setName('user')
            .setDescription('User to hardmute')
            .setRequired(true))
    .addStringOption(option =>
        option.setName('duration')
            .setDescription('Duration: 30m, 2h, 7d (empty = indefinite)')
            .setRequired(false))
    .addStringOption(option =>
        option.setName('reason')
            .setDescription('Reason')
            .setRequired(false))
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageRoles);

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const usuario = interaction.options.getUser('user');
    const duracionStr = interaction.options.getString('duration');
    const motivo = interaction.options.getString('reason') || MessagesService.get(guildId, 'Moderacion:MotivoPorDefecto');

    if (usuario.id === interaction.user.id) {
        return interaction.reply({ content: MessagesService.get(guildId, 'Moderacion:Errores:MismoUsuario'), ephemeral: true });
    }
    if (usuario.id === interaction.client.user.id) {
        return interaction.reply({ content: MessagesService.get(guildId, 'Moderacion:Errores:AlBot'), ephemeral: true });
    }

    let durationMs = null;
    let expiresAtIso = null;

    if (duracionStr) {
        durationMs = parseDuration(duracionStr);
        if (!durationMs || durationMs <= 0) {
            return interaction.reply({ content: MessagesService.get(guildId, 'Moderacion:Errores:DuracionInvalida'), ephemeral: true });
        }
        expiresAtIso = new Date(Date.now() + durationMs).toISOString();
    }

    try {
        const member = await interaction.guild.members.fetch(usuario.id).catch(() => null);
        if (!member) {
            return interaction.reply({ content: MessagesService.get(guildId, 'Moderacion:Errores:NoEnServidor', { usuario: usuario.username }), ephemeral: true });
        }

        if (interaction.guild.ownerId !== interaction.user.id && member.roles.highest.position >= interaction.member.roles.highest.position) {
            return interaction.reply({ content: MessagesService.get(guildId, 'Moderacion:Errores:Jerarquia', { usuario: usuario.username }), ephemeral: true });
        }
        if (member.roles.highest.position >= interaction.guild.members.me.roles.highest.position) {
            return interaction.reply({ content: MessagesService.get(guildId, 'Moderacion:Errores:Jerarquia', { usuario: usuario.username }), ephemeral: true });
        }

        await interaction.deferReply();

        // 1. Guardar y quitar roles que estén por debajo del bot
        const rolesQuitar = member.roles.cache.filter(r =>
            r.id !== interaction.guild.roles.everyone.id &&
            !r.managed &&
            r.position < interaction.guild.members.me.roles.highest.position
        );

        const roleIdsText = rolesQuitar.size > 0 ? rolesQuitar.map(r => r.id).join(',') : '';
        const existing = db.prepare('SELECT Id FROM HardmuteBackups WHERE GuildId = ? AND UserId = ?').get(guildId, usuario.id);
        if (existing) {
            db.prepare('UPDATE HardmuteBackups SET RoleIds = ?, ExpiresAt = ?, CreatedAt = ? WHERE GuildId = ? AND UserId = ?')
                .run(roleIdsText, expiresAtIso, new Date().toISOString(), guildId, usuario.id);
        } else {
            db.prepare('INSERT INTO HardmuteBackups (GuildId, UserId, RoleIds, ExpiresAt, CreatedAt) VALUES (?, ?, ?, ?, ?)')
                .run(guildId, usuario.id, roleIdsText, expiresAtIso, new Date().toISOString());
        }

        for (const [id, rol] of rolesQuitar) {
            try {
                await member.roles.remove(rol, `Hardmute por ${interaction.user.username}`);
            } catch {}
        }

        // 2. Denegar permisos en todos los canales
        const channels = await interaction.guild.channels.fetch();
        for (const [id, channel] of channels) {
            if (!channel) continue;
            if (
                channel.type === ChannelType.GuildText ||
                channel.type === ChannelType.GuildVoice ||
                channel.type === ChannelType.GuildForum ||
                channel.type === ChannelType.GuildStageVoice ||
                channel.type === ChannelType.PublicThread ||
                channel.type === ChannelType.PrivateThread
            ) {
                try {
                    await channel.permissionOverwrites.edit(member, {
                        SendMessages: false,
                        Speak: false,
                        SendMessagesInThreads: false
                    }, { reason: `Hardmute por ${interaction.user.username}: ${motivo}` });
                } catch {}
            }
        }

        if (expiresAtIso) {
            scheduleUnhardmute(interaction.client, guildId, usuario.id, expiresAtIso);
        }

        await notifyMemberDm(member, 'Hardmute', motivo);

        const incidente = registerIncident(guildId, usuario, interaction.user, IncidentType.Hardmute, motivo, duracionStr);
        await announceIncident(interaction.guild, incidente);

        const exito = MessagesService.get(guildId, 'Moderacion:Exito:Hardmute', { usuario: usuario.username });
        const duracionNota = duracionStr ? ` (${duracionStr})` : '';
        const formato = MessagesService.get(guildId, 'Moderacion:Exito:Formato', { texto: `${exito}${duracionNota}`, motivo });
        const caso = MessagesService.get(guildId, 'Moderacion:Caso', { caso: incidente.id });

        await interaction.editReply({ content: `${formato}\n*${caso}*` });
    } catch (error) {
        console.error('[hardmute] Error:', error);
        await interaction.editReply({ content: MessagesService.get(guildId, 'Errores:Interno') });
    }
}
