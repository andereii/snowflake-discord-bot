import { SlashCommandBuilder, EmbedBuilder } from 'discord.js';
import db from '../services/database.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
  .setName('birthday')
  .setDescription('Register your birthday')
  .addStringOption(option =>
    option.setName('date')
      .setDescription('Date in format DD/MM/YYYY or MM/DD/YYYY')
      .setRequired(true)
  )
  .addBooleanOption(option =>
    option.setName('show_year')
      .setDescription('Include birth year')
      .setRequired(false)
  );

function tryParseFecha(texto, lang) {
  const partes = texto.replace('-', '/').split('/');
  if (partes.length < 2 || partes.length > 3) return null;

  const primeroEsMes = lang !== 'es' && lang !== 'pt';
  const a = parseInt(partes[0]);
  const b = parseInt(partes[1]);
  
  if (isNaN(a) || isNaN(b)) return null;

  let dia, mes;
  if (primeroEsMes) {
    mes = a;
    dia = b;
  } else {
    dia = a;
    mes = b;
  }

  const anio = partes.length === 3 ? parseInt(partes[2]) : null;
  if (partes.length === 3 && isNaN(anio)) return null;

  return { dia, mes, anio };
}

export async function execute(interaction) {
  const guildId = interaction.guildId;
  const fecha = interaction.options.getString('date');
  const lang = MessagesService.locale(guildId);

  const parsed = tryParseFecha(fecha, lang);
  if (!parsed) {
    return await interaction.reply({
      content: MessagesService.get(guildId, 'Cumple:ErrorFormato'),
      ephemeral: true
    });
  }

  const { dia, mes, anio } = parsed;

  if (mes < 1 || mes > 12) {
    return await interaction.reply({ content: MessagesService.get(guildId, 'Cumple:ErrorMes'), ephemeral: true });
  }
  if (dia < 1 || dia > 31) {
    return await interaction.reply({ content: MessagesService.get(guildId, 'Cumple:ErrorDia'), ephemeral: true });
  }
  const diasPorMes = [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  if (dia > diasPorMes[mes - 1]) {
    return await interaction.reply({ content: MessagesService.get(guildId, 'Cumple:ErrorFechaInvalida'), ephemeral: true });
  }
  if (anio && (anio < 1900 || anio > new Date().getFullYear())) {
    return await interaction.reply({ content: MessagesService.get(guildId, 'Cumple:ErrorAnio'), ephemeral: true });
  }

  const existente = db.prepare('SELECT * FROM Birthdays WHERE GuildId = ? AND UserId = ?').get(guildId, interaction.user.id);
  
  if (existente) {
    db.prepare('UPDATE Birthdays SET Day = ?, Month = ?, Year = ? WHERE GuildId = ? AND UserId = ?')
      .run(dia, mes, anio, guildId, interaction.user.id);
  } else {
    db.prepare('INSERT INTO Birthdays (GuildId, UserId, Day, Month, Year) VALUES (?, ?, ?, ?, ?)')
      .run(guildId, interaction.user.id, dia, mes, anio);
  }

  const desc = anio
    ? MessagesService.get(guildId, 'Cumple:RegistradoConAnio', { dia, mes, anio })
    : MessagesService.get(guildId, 'Cumple:Registrado', { dia, mes });

  const embed = new EmbedBuilder()
    .setTitle(MessagesService.get(guildId, 'Cumple:Titulo'))
    .setDescription(desc)
    .setColor(0xff00ff);

  await interaction.reply({ embeds: [embed] });
}
