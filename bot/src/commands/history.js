import { SlashCommandBuilder, EmbedBuilder, PermissionFlagsBits } from 'discord.js';
import db from '../services/database.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
    .setName('history')
    .setDescription('Show a user\'s incidents (or the server\'s latest)')
    .addUserOption(option =>
        option.setName('user')
            .setDescription('User to look up (empty = server\'s latest)')
            .setRequired(false)
    )
    .setDefaultMemberPermissions(PermissionFlagsBits.ModerateMembers);

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const usuario = interaction.options.getUser('user');

    let rows;
    if (usuario) {
        rows = db.prepare(`
            SELECT Id, CAST(TargetUserId AS TEXT) as TargetUserId, TargetTag, 
                   CAST(ModeratorId AS TEXT) as ModeratorId, ModeratorTag, 
                   Type, Reason, Duration, CreatedAt 
            FROM Incidents 
            WHERE GuildId = ? AND TargetUserId = ? 
            ORDER BY Id DESC LIMIT 10
        `).all(guildId, usuario.id);
    } else {
        rows = db.prepare(`
            SELECT Id, CAST(TargetUserId AS TEXT) as TargetUserId, TargetTag, 
                   CAST(ModeratorId AS TEXT) as ModeratorId, ModeratorTag, 
                   Type, Reason, Duration, CreatedAt 
            FROM Incidents 
            WHERE GuildId = ? 
            ORDER BY Id DESC LIMIT 10
        `).all(guildId);
    }

    const title = usuario
        ? MessagesService.get(guildId, 'Moderacion:Historial:TituloUsuario', { usuario: usuario.username })
        : MessagesService.get(guildId, 'Moderacion:Historial:TituloServidor');

    const embed = new EmbedBuilder()
        .setTitle(title)
        .setColor(0x5865F2);

    if (!rows || rows.length === 0) {
        embed.setDescription(MessagesService.get(guildId, 'Moderacion:Historial:Vacio'));
    } else {
        for (const i of rows) {
            const tipoLabel = MessagesService.get(guildId, `Moderacion:Tipos:${i.Type}`);
            const duracionStr = i.Duration ? ` · ${i.Duration}` : '';
            const timestamp = Math.floor(new Date(i.CreatedAt).getTime() / 1000);
            const fechaStr = `<t:${timestamp}:d>`;

            const cabecera = MessagesService.get(guildId, 'Moderacion:Historial:CabeceraCaso', {
                caso: i.Id,
                tipo: tipoLabel,
                duracion: duracionStr,
                fecha: fechaStr
            });

            const userDisplay = i.TargetTag ? `<@${i.TargetUserId}> (${i.TargetTag})` : `<@${i.TargetUserId}>`;
            const modDisplay = i.ModeratorTag ? `<@${i.ModeratorId}> (${i.ModeratorTag})` : `<@${i.ModeratorId}>`;

            const linea = MessagesService.get(guildId, 'Moderacion:Historial:Linea', {
                usuario: userDisplay,
                moderador: modDisplay,
                motivo: i.Reason || MessagesService.get(guildId, 'Moderacion:MotivoPorDefecto')
            });

            embed.addFields({ name: cabecera, value: linea, inline: false });
        }
    }

    await interaction.reply({ embeds: [embed], ephemeral: true });
}
