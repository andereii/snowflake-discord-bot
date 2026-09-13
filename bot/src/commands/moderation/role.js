import { PermissionFlagsBits } from 'discord.js';
import { slash } from '../../lib/slash.js';
import { esPt, localize } from '../../lib/localize.js';
import { changeMemberRole } from '../../services/roles.js';
import { scheduleTimedRole, cancelTimedRole } from '../../services/timedRoles.js';
import { parseDuration, formatCompactDuration, MAX_TEMP_ROLE_MS } from '../../lib/duration.js';
import MessagesService from '../../services/messagesService.js';

function sub(builder, name, description, { names, descriptions } = {}) {
    builder.setName(name).setDescription(description);
    localize(builder, { name: names, description: descriptions });
    return builder;
}

export const data = slash('role', 'Manage user roles', {
    names: esPt('rol', 'cargo'),
    descriptions: esPt('Gestiona roles de usuarios', 'Gerencia cargos de usuários'),
    permissions: PermissionFlagsBits.ManageRoles
})
    .addSubcommand(s => sub(s, 'add', 'Add a role to a user', {
        names: esPt('agregar', 'adicionar'),
        descriptions: esPt('Añade un rol a un usuario', 'Adiciona um cargo a um usuário')
    })
        .addUserOption(o => o.setName('user').setDescription('User to receive the role').setRequired(true)
            .setNameLocalizations(esPt('usuario'))
            .setDescriptionLocalizations(esPt('Usuario al que se le dará el rol', 'Usuário que receberá o cargo')))
        .addRoleOption(o => o.setName('role').setDescription('Role to add').setRequired(true)
            .setNameLocalizations(esPt('rol', 'cargo'))
            .setDescriptionLocalizations(esPt('Rol a asignar', 'Cargo a atribuir')))
        .addStringOption(o => o.setName('duration').setDescription('How long to keep the role (1m, 12h, 4d)')
            .setNameLocalizations(esPt('duracion', 'duracao'))
            .setDescriptionLocalizations(esPt(
                'Cuánto tiempo conservar el rol (1m, 12h, 4d)',
                'Por quanto tempo manter o cargo (1m, 12h, 4d)'
            ))))
    .addSubcommand(s => sub(s, 'remove', 'Remove a role from a user', {
        names: esPt('quitar', 'remover'),
        descriptions: esPt('Quita un rol a un usuario', 'Remove um cargo de um usuário')
    })
        .addUserOption(o => o.setName('user').setDescription('User to remove the role from').setRequired(true)
            .setNameLocalizations(esPt('usuario'))
            .setDescriptionLocalizations(esPt('Usuario al que se le quitará el rol', 'Usuário de quem o cargo será removido')))
        .addRoleOption(o => o.setName('role').setDescription('Role to remove').setRequired(true)
            .setNameLocalizations(esPt('rol', 'cargo'))
            .setDescriptionLocalizations(esPt('Rol a quitar', 'Cargo a remover'))));

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const add = interaction.options.getSubcommand() === 'add';
    const user = interaction.options.getUser('user');
    const role = interaction.options.getRole('role');
    const member = await interaction.guild.members.fetch(user.id).catch(() => null);

    if (!member) {
        return interaction.reply({
            content: MessagesService.get(guildId, 'Moderacion:Errores:NoEnServidor', { usuario: user.username }),
            ephemeral: true
        });
    }

    let durationMs = null;
    if (add) {
        const rawDuration = interaction.options.getString('duration');
        if (rawDuration) {
            durationMs = parseDuration(rawDuration);
            if (!durationMs || durationMs < 1000 || durationMs > MAX_TEMP_ROLE_MS) {
                return interaction.reply({
                    content: MessagesService.get(guildId, 'Roles:DuracionInvalida'),
                    ephemeral: true
                });
            }
        }
    }

    const result = await changeMemberRole({
        guild: interaction.guild,
        actor: interaction.member,
        member,
        role,
        add,
        reason: `${add ? 'Added' : 'Removed'} by ${interaction.user.username} (${interaction.user.id})`
    });

    if (!result.ok) {
        return interaction.reply({
            content: MessagesService.get(guildId, result.key, result.vars),
            ephemeral: true
        });
    }

    if (add && durationMs && (result.key === 'Roles:Asignado' || result.key === 'Roles:YaTiene')) {
        scheduleTimedRole(guildId, member.id, role.id, durationMs);
        return interaction.reply(MessagesService.get(guildId, 'Roles:AsignadoTemporal', {
            ...result.vars,
            duracion: formatCompactDuration(durationMs)
        }));
    }

    if (!add && result.key === 'Roles:Removido') {
        cancelTimedRole(guildId, member.id, role.id);
    }

    return interaction.reply(MessagesService.get(guildId, result.key, result.vars));
}
