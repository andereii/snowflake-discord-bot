import {
    ActionRowBuilder,
    EmbedBuilder,
    StringSelectMenuBuilder,
    StringSelectMenuOptionBuilder
} from 'discord.js';
import db from './database.js';
import MessagesService from './messagesService.js';

export const COLOR_SELECT_ID = 'snowflake_colores';

const PALETTES = {
    normal: [
        ['Rojo', 'E74C3C'], ['Naranja', 'E67E22'], ['Amarillo', 'F1C40F'], ['Lima', '82E0AA'],
        ['Verde', '2ECC71'], ['Esmeralda', '1ABC9C'], ['Cian', '3498DB'], ['Azul', '2980B9'],
        ['Índigo', '5B6BE1'], ['Violeta', '9B59B6'], ['Magenta', 'CC00FF'], ['Rosa', 'E91E63'],
        ['Fucsia', 'FF1493'], ['Morado', '71368A'], ['Blanco', 'F2F2F2'], ['Gris', '95A5A6'], ['Negro', '4A4E69']
    ],
    pastel: [
        ['Rosa pastel', 'FFB6C1'], ['Chicle', 'FFC1CC'], ['Melocotón', 'FFDAB9'], ['Coral', 'FFA07A'],
        ['Arena', 'FFE4B5'], ['Vainilla', 'F3E5AB'], ['Limón', 'FFFACD'], ['Naranja pastel', 'FFE5CC'],
        ['Verde pastel', 'A8E6CF'], ['Menta', 'B5E7A0'], ['Aqua', '99E2E2'], ['Cielo', 'ADD8E6'],
        ['Azul pastel', 'B4C5E4'], ['Lavanda', 'E6E6FA'], ['Lila', 'C8A2C8'], ['Malva', 'D8BFD8'], ['Perla', 'E8E8E8']
    ]
};

db.exec(`
CREATE TABLE IF NOT EXISTS ColorRoles (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    GuildId INTEGER NOT NULL,
    RoleId INTEGER NOT NULL,
    Name TEXT NOT NULL,
    ColorHex TEXT NOT NULL
);
`);

function listInstalled(guildId) {
    return db.prepare(`
        SELECT Id, CAST(GuildId AS TEXT) AS GuildId, CAST(RoleId AS TEXT) AS RoleId, Name, ColorHex
        FROM ColorRoles WHERE GuildId = ? ORDER BY Id ASC
    `).all(guildId);
}

export async function installPalette(guild, palette = 'normal') {
    const colors = PALETTES[palette] || PALETTES.normal;
    const names = new Set(colors.map(([name]) => name));
    const existing = listInstalled(guild.id);

    let removed = 0;
    for (const row of existing.filter(c => !names.has(c.Name))) {
        const role = guild.roles.cache.get(row.RoleId);
        if (role) {
            await role.delete('Color palette change').catch(() => {});
            removed += 1;
        }
        db.prepare('DELETE FROM ColorRoles WHERE Id = ?').run(row.Id);
    }

    const remainingNames = new Set(listInstalled(guild.id).map(c => c.Name));
    let created = 0;
    for (const [name, hex] of colors) {
        if (remainingNames.has(name)) continue;
        try {
            const role = await guild.roles.create({
                name: `• ${name}`,
                color: parseInt(hex, 16),
                hoist: false,
                mentionable: false,
                reason: 'Color palette installed'
            });
            db.prepare('INSERT INTO ColorRoles (GuildId, RoleId, Name, ColorHex) VALUES (?, ?, ?, ?)')
                .run(guild.id, role.id, name, hex);
            created += 1;
        } catch (err) {
            console.warn('[colors] could not create role', name, err.message);
        }
    }

    return { created, removed, total: colors.length };
}

export async function uninstallPalette(guild) {
    const existing = listInstalled(guild.id);
    let deleted = 0;
    for (const row of existing) {
        const role = guild.roles.cache.get(row.RoleId);
        if (role) {
            await role.delete('Uninstalling color palette').catch(() => {});
            deleted += 1;
        }
    }
    db.prepare('DELETE FROM ColorRoles WHERE GuildId = ?').run(guild.id);
    return deleted;
}

export async function removeMemberColor(member, guildId) {
    const installed = listInstalled(guildId);
    let had = false;
    for (const row of installed) {
        if (member.roles.cache.has(row.RoleId)) {
            await member.roles.remove(row.RoleId, 'User removed color').catch(() => {});
            had = true;
        }
    }
    return had;
}

export function listColors(guildId) {
    return listInstalled(guildId);
}

export function buildChooser(guild) {
    const roles = listInstalled(guild.id);
    if (!roles.length) return null;

    const options = roles.slice(0, 24).map(row =>
        new StringSelectMenuOptionBuilder()
            .setLabel(`• ${row.Name}`.slice(0, 100))
            .setValue(row.RoleId)
            .setDescription(`#${row.ColorHex}`)
    );
    options.push(
        new StringSelectMenuOptionBuilder()
            .setLabel(MessagesService.get(guild.id, 'Colores:QuitarOpcion'))
            .setValue('0')
            .setDescription(MessagesService.get(guild.id, 'Colores:QuitarHint'))
    );

    const select = new StringSelectMenuBuilder()
        .setCustomId(COLOR_SELECT_ID)
        .setPlaceholder(MessagesService.get(guild.id, 'Colores:Placeholder'))
        .addOptions(options);

    const embed = new EmbedBuilder()
        .setTitle(MessagesService.get(guild.id, 'Colores:Titulo'))
        .setDescription(MessagesService.get(guild.id, 'Colores:Descripcion'))
        .setColor(0x3498DB);

    return {
        embeds: [embed],
        components: [new ActionRowBuilder().addComponents(select)]
    };
}

export async function applyColorSelection(member, guild, roleId) {
    const installed = listInstalled(guild.id);
    for (const row of installed) {
        if (member.roles.cache.has(row.RoleId)) {
            await member.roles.remove(row.RoleId, 'Color change').catch(() => {});
        }
    }
    if (!roleId || roleId === '0') return null;
    const role = guild.roles.cache.get(roleId);
    if (!role) return null;
    await member.roles.add(role, 'Color chosen');
    return role.name;
}

export async function handleColorSelect(interaction) {
    const guildId = interaction.guildId;
    const value = interaction.values?.[0];
    if (!value) return;
    try {
        const member = interaction.member ?? await interaction.guild.members.fetch(interaction.user.id);
        const name = await applyColorSelection(member, interaction.guild, value);
        const text = value === '0'
            ? MessagesService.get(guildId, 'Colores:Quitado')
            : MessagesService.get(guildId, 'Colores:Aplicado', { color: name || '—' });
        await interaction.update({ content: text, embeds: [], components: [] });
    } catch (err) {
        console.error('[colors] select failed:', err);
        await interaction.update({
            content: MessagesService.get(guildId, 'Colores:Error'),
            embeds: [],
            components: []
        }).catch(() => {});
    }
}
