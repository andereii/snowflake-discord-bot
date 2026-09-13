export async function changeMemberRole({ guild, actor, member, role, add, reason }) {
    if (!member) return { ok: false, key: 'Moderacion:Errores:NoEnServidor', vars: { usuario: '—' } };

    const bot = guild.members.me;
    if (!bot) return { ok: false, key: 'Errores:SinPermisos', vars: {} };

    if (role.id === guild.id || role.managed || bot.roles.highest.position <= role.position) {
        return { ok: false, key: 'Roles:JerarquiaBot', vars: { rol: role.name } };
    }

    const isOwner = guild.ownerId === actor.id;
    if (!isOwner && actor.roles.highest.position <= role.position) {
        return { ok: false, key: 'Roles:JerarquiaUsuario', vars: { rol: role.name } };
    }

    const hasRole = member.roles.cache.has(role.id);
    if (add && hasRole) {
        return { ok: true, key: 'Roles:YaTiene', vars: { usuario: member.displayName, rol: role.name } };
    }
    if (!add && !hasRole) {
        return { ok: true, key: 'Roles:NoTiene', vars: { usuario: member.displayName, rol: role.name } };
    }

    if (add) await member.roles.add(role, reason);
    else await member.roles.remove(role, reason);

    return {
        ok: true,
        key: add ? 'Roles:Asignado' : 'Roles:Removido',
        vars: { usuario: member.displayName, rol: role.name }
    };
}
