import { EmbedBuilder } from 'discord.js';
import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import MessagesService from '../../services/messagesService.js';
import db from '../../services/database.js';

const DAYS_IN_MONTH = [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

function parseDate(text, lang) {
    const parts = text.replace('-', '/').split('/');
    if (parts.length < 2 || parts.length > 3) return null;
    const monthFirst = lang !== 'es' && lang !== 'pt';
    const a = parseInt(parts[0], 10);
    const b = parseInt(parts[1], 10);
    if (Number.isNaN(a) || Number.isNaN(b)) return null;
    const day = monthFirst ? b : a;
    const month = monthFirst ? a : b;
    const year = parts.length === 3 ? parseInt(parts[2], 10) : null;
    if (parts.length === 3 && Number.isNaN(year)) return null;
    return { day, month, year };
}

export const commands = [
    {
        data: slash('birthday', 'Register your birthday', {
            names: esPt('cumpleaños', 'aniversario'),
            descriptions: esPt('Registra tu fecha de cumpleaños', 'Registra sua data de aniversário')
        })
            .addStringOption(o => o.setName('date').setDescription('Date in DD/MM/YYYY or MM/DD/YYYY').setRequired(true))
            .addBooleanOption(o => o.setName('show_year').setDescription('Include birth year')),
        async execute(interaction) {
            const guildId = interaction.guildId;
            const lang = MessagesService.locale(guildId);
            const parsed = parseDate(interaction.options.getString('date'), lang);
            if (!parsed) {
                return interaction.reply({ content: MessagesService.get(guildId, 'Cumple:ErrorFormato'), ephemeral: true });
            }

            const { day, month, year } = parsed;
            if (month < 1 || month > 12) {
                return interaction.reply({ content: MessagesService.get(guildId, 'Cumple:ErrorMes'), ephemeral: true });
            }
            if (day < 1 || day > 31 || day > DAYS_IN_MONTH[month - 1]) {
                return interaction.reply({ content: MessagesService.get(guildId, 'Cumple:ErrorFechaInvalida'), ephemeral: true });
            }
            if (year && (year < 1900 || year > new Date().getFullYear())) {
                return interaction.reply({ content: MessagesService.get(guildId, 'Cumple:ErrorAnio'), ephemeral: true });
            }

            const existing = db.prepare('SELECT * FROM Birthdays WHERE GuildId = ? AND UserId = ?')
                .get(guildId, interaction.user.id);
            if (existing) {
                db.prepare('UPDATE Birthdays SET Day = ?, Month = ?, Year = ? WHERE GuildId = ? AND UserId = ?')
                    .run(day, month, year, guildId, interaction.user.id);
            } else {
                db.prepare('INSERT INTO Birthdays (GuildId, UserId, Day, Month, Year) VALUES (?, ?, ?, ?, ?)')
                    .run(guildId, interaction.user.id, day, month, year);
            }

            const desc = year
                ? MessagesService.get(guildId, 'Cumple:RegistradoConAnio', { dia: day, mes: month, anio: year })
                : MessagesService.get(guildId, 'Cumple:Registrado', { dia: day, mes: month });

            await interaction.reply({
                embeds: [
                    new EmbedBuilder()
                        .setTitle(MessagesService.get(guildId, 'Cumple:Titulo'))
                        .setDescription(desc)
                        .setColor(0xff00ff)
                ]
            });
        }
    },
    {
        data: slash('birthday-remove', 'Delete your registered birthday from this server', {
            names: esPt('cumpleaños-quitar', 'aniversario-remover'),
            descriptions: esPt('Elimina tu fecha de cumpleaños', 'Remove sua data de aniversário')
        }),
        async execute(interaction) {
            const guildId = interaction.guildId;
            const result = db.prepare('DELETE FROM Birthdays WHERE GuildId = ? AND UserId = ?')
                .run(guildId, interaction.user.id);
            await interaction.reply({
                content: MessagesService.get(guildId, result.changes > 0 ? 'Cumple:Quitado' : 'Cumple:NoRegistrado'),
                ephemeral: true
            });
        }
    }
];
