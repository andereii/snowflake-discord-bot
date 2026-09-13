import { EmbedBuilder, PermissionFlagsBits } from 'discord.js';
import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import MessagesService from '../../services/messagesService.js';
import db from '../../services/database.js';

const INCIDENT_SELECT = `
    SELECT Id, CAST(TargetUserId AS TEXT) as TargetUserId, TargetTag,
           CAST(ModeratorId AS TEXT) as ModeratorId, ModeratorTag,
           Type, Reason, Duration, CreatedAt
    FROM Incidents
`;

export const data = slash('history', "Show a user's incidents (or the server's latest)", {
    names: esPt('historial', 'historico'),
    descriptions: esPt(
        'Muestra los incidentes de un usuario (o los últimos del servidor)',
        'Mostra os incidentes de um usuário (ou os últimos do servidor)'
    ),
    permissions: PermissionFlagsBits.ModerateMembers
}).addUserOption(o => o.setName('user').setDescription("User to look up (empty = server's latest)"));

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const user = interaction.options.getUser('user');

    const rows = user
        ? db.prepare(`${INCIDENT_SELECT} WHERE GuildId = ? AND TargetUserId = ? ORDER BY Id DESC LIMIT 10`).all(guildId, user.id)
        : db.prepare(`${INCIDENT_SELECT} WHERE GuildId = ? ORDER BY Id DESC LIMIT 10`).all(guildId);

    const title = user
        ? MessagesService.get(guildId, 'Moderacion:Historial:TituloUsuario', { usuario: user.username })
        : MessagesService.get(guildId, 'Moderacion:Historial:TituloServidor');

    const embed = new EmbedBuilder().setTitle(title).setColor(0x5865F2);

    if (!rows.length) {
        embed.setDescription(MessagesService.get(guildId, 'Moderacion:Historial:Vacio'));
    } else {
        for (const row of rows) {
            const typeLabel = MessagesService.get(guildId, `Moderacion:Tipos:${row.Type}`);
            const duration = row.Duration ? ` · ${row.Duration}` : '';
            const date = `<t:${Math.floor(new Date(row.CreatedAt).getTime() / 1000)}:d>`;
            const header = MessagesService.get(guildId, 'Moderacion:Historial:CabeceraCaso', {
                caso: row.Id, tipo: typeLabel, duracion: duration, fecha: date
            });
            const userDisplay = row.TargetTag ? `<@${row.TargetUserId}> (${row.TargetTag})` : `<@${row.TargetUserId}>`;
            const modDisplay = row.ModeratorTag ? `<@${row.ModeratorId}> (${row.ModeratorTag})` : `<@${row.ModeratorId}>`;
            const line = MessagesService.get(guildId, 'Moderacion:Historial:Linea', {
                usuario: userDisplay,
                moderador: modDisplay,
                motivo: row.Reason || MessagesService.get(guildId, 'Moderacion:MotivoPorDefecto')
            });
            embed.addFields({ name: header, value: line, inline: false });
        }
    }

    await interaction.reply({ embeds: [embed], ephemeral: true });
}
