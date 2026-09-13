import { ChannelType, EmbedBuilder, PermissionFlagsBits } from 'discord.js';
import { slash } from '../../lib/slash.js';
import { esPt, localize } from '../../lib/localize.js';
import afkService from '../../services/afk.js';
import MessagesService from '../../services/messagesService.js';

function sub(builder, name, description, { names, descriptions } = {}) {
    builder.setName(name).setDescription(description);
    localize(builder, { name: names, description: descriptions });
    return builder;
}

export const data = slash('afk', 'Manage AFK (Away From Keyboard) status', {
    names: esPt('afk'),
    descriptions: esPt('Gestiona el estado de ausencia (AFK)', 'Gerencia o estado de ausência (AFK)')
})
    .addSubcommand(s => sub(s, 'set', 'Set your AFK status with an optional reason', {
        descriptions: esPt('Establece tu estado ausente con un motivo opcional', 'Define seu estado ausente com um motivo opcional')
    }).addStringOption(o => o.setName('reason').setDescription('Reason for being AFK')
        .setNameLocalizations(esPt('motivo'))
        .setDescriptionLocalizations(esPt('Motivo de la ausencia', 'Motivo da ausência'))))
    .addSubcommand(s => sub(s, 'list', 'Show the list of currently AFK members', {
        descriptions: esPt('Muestra la lista de miembros ausentes', 'Mostra a lista de membros ausentes')
    }))
    .addSubcommand(s => sub(s, 'ignore', 'Add a channel where AFK status will not be removed', {
        descriptions: esPt('Añade un canal donde no se quitará el AFK', 'Adiciona um canal onde o AFK não será removido')
    }).addChannelOption(o => o.setName('channel').setDescription('Channel to ignore').setRequired(true)
        .addChannelTypes(ChannelType.GuildText)
        .setNameLocalizations(esPt('canal'))
        .setDescriptionLocalizations(esPt('Canal a ignorar', 'Canal a ignorar'))))
    .addSubcommand(s => sub(s, 'unignore', 'Remove a channel from the AFK ignored list', {
        descriptions: esPt('Quita un canal de la lista de ignorados', 'Remove um canal da lista de ignorados')
    }).addChannelOption(o => o.setName('channel').setDescription('Channel to unignore').setRequired(true)
        .addChannelTypes(ChannelType.GuildText)
        .setNameLocalizations(esPt('canal'))
        .setDescriptionLocalizations(esPt('Canal a quitar de ignorados', 'Canal a remover da lista de ignorados'))))
    .addSubcommand(s => sub(s, 'ignored', 'Show AFK ignored channels', {
        descriptions: esPt('Muestra los canales ignorados de AFK', 'Mostra os canais ignorados de AFK')
    }))
    .addSubcommand(s => sub(s, 'remove', 'Remove a member from the AFK list', {
        descriptions: esPt('Quita a un miembro de la lista de ausentes', 'Remove um membro da lista de ausentes')
    }).addUserOption(o => o.setName('user').setDescription('Member to remove from AFK').setRequired(true)
        .setNameLocalizations(esPt('usuario'))
        .setDescriptionLocalizations(esPt('Miembro a quitar de ausentes', 'Membro a remover dos ausentes'))))
    .addSubcommand(s => sub(s, 'removeall', 'Remove all members from the AFK list', {
        descriptions: esPt('Quita a todos de la lista de ausentes', 'Remove todos da lista de ausentes')
    }))
    .addSubcommand(s => sub(s, 'reset', "Reset an AFK member's reason to default", {
        descriptions: esPt('Restablece el motivo AFK de un miembro', 'Redefine o motivo AFK de um membro')
    }).addUserOption(o => o.setName('user').setDescription('Member whose AFK reason will be reset').setRequired(true)
        .setNameLocalizations(esPt('usuario'))
        .setDescriptionLocalizations(esPt('Miembro al que se restablecerá el motivo', 'Membro que terá o motivo redefinido'))));

function requireManageGuild(interaction) {
    if (interaction.member?.permissions?.has(PermissionFlagsBits.ManageGuild)) return true;
    return false;
}

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const subName = interaction.options.getSubcommand();
    const adminSubs = new Set(['ignore', 'unignore', 'ignored', 'remove', 'removeall', 'reset']);
    if (adminSubs.has(subName) && !requireManageGuild(interaction)) {
        return interaction.reply({
            content: MessagesService.get(guildId, 'Errores:SinPermisos'),
            ephemeral: true
        });
    }

    if (subName === 'set') {
        const reason = await afkService.setAfk(interaction.member, interaction.options.getString('reason'));
        const embed = new EmbedBuilder()
            .setTitle(interaction.user.username)
            .setThumbnail(interaction.user.displayAvatarURL())
            .setDescription(`💤 ${MessagesService.get(guildId, 'Afk:Establecido', { motivo: reason })}`)
            .setColor(0x6495ED);
        return interaction.reply({ embeds: [embed] });
    }

    if (subName === 'list') {
        const ausentes = afkService.listAfk(guildId);
        if (!ausentes.length) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'Afk:SinMiembrosAusentes'),
                ephemeral: true
            });
        }
        const lines = ausentes.map(a =>
            `• <@${a.userId}> — *"${a.reason}"* (<t:${Math.floor(a.timestamp / 1000)}:R>)`
        );
        let body = lines.join('\n');
        if (body.length > 3900) body = body.slice(0, 3897) + '…';
        const embed = new EmbedBuilder()
            .setTitle(`💤 ${MessagesService.get(guildId, 'Afk:TituloMiembrosAusentes')}`)
            .setDescription(body)
            .setColor(0x6495ED)
            .setFooter({ text: `Total: ${ausentes.length}` });
        return interaction.reply({ embeds: [embed] });
    }

    if (subName === 'ignore') {
        const channel = interaction.options.getChannel('channel');
        const added = afkService.addIgnoredChannel(guildId, channel.id);
        const key = added ? 'Afk:CanalIgnorado' : 'Afk:CanalYaIgnorado';
        return interaction.reply({
            content: MessagesService.get(guildId, key, { canal: channel.toString() }),
            ephemeral: !added
        });
    }

    if (subName === 'unignore') {
        const channel = interaction.options.getChannel('channel');
        const removed = afkService.removeIgnoredChannel(guildId, channel.id);
        const key = removed ? 'Afk:CanalDesignorado' : 'Afk:CanalNoIgnorado';
        return interaction.reply({
            content: MessagesService.get(guildId, key, { canal: channel.toString() }),
            ephemeral: !removed
        });
    }

    if (subName === 'ignored') {
        const ids = afkService.listIgnoredChannels(guildId);
        if (!ids.length) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'Afk:SinCanalesIgnorados'),
                ephemeral: true
            });
        }
        const embed = new EmbedBuilder()
            .setTitle(`🔇 ${MessagesService.get(guildId, 'Afk:TituloCanalesIgnorados')}`)
            .setDescription(ids.map(id => `• <#${id}> (\`${id}\`)`).join('\n'))
            .setColor(0x6495ED)
            .setFooter({ text: `Total: ${ids.length}` });
        return interaction.reply({ embeds: [embed] });
    }

    if (subName === 'remove') {
        const user = interaction.options.getUser('user');
        const member = await interaction.guild.members.fetch(user.id).catch(() => null);
        if (!member) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'Moderacion:Errores:NoEnServidor', { usuario: user.username }),
                ephemeral: true
            });
        }
        const removed = await afkService.removeAfk(member);
        const key = removed ? 'Afk:RemovidoMod' : 'Afk:NoEstaAusente';
        return interaction.reply({
            content: MessagesService.get(guildId, key, { usuario: member.displayName }),
            ephemeral: !removed
        });
    }

    if (subName === 'removeall') {
        const total = await afkService.removeAllAfk(interaction.guild);
        if (total > 0) {
            return interaction.reply(MessagesService.get(guildId, 'Afk:RemovidosTodos', { total }));
        }
        return interaction.reply({
            content: MessagesService.get(guildId, 'Afk:SinMiembrosAusentes'),
            ephemeral: true
        });
    }

    if (subName === 'reset') {
        const user = interaction.options.getUser('user');
        const ok = afkService.resetAfkReason(guildId, user.id);
        const member = await interaction.guild.members.fetch(user.id).catch(() => null);
        const name = member?.displayName || user.username;
        const key = ok ? 'Afk:MotivoReseteado' : 'Afk:NoEstaAusente';
        return interaction.reply({
            content: MessagesService.get(guildId, key, { usuario: name }),
            ephemeral: !ok
        });
    }
}
