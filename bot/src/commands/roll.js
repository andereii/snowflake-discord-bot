import { SlashCommandBuilder, EmbedBuilder } from 'discord.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
  .setName('roll')
  .setDescription('Roll a die')
  .addIntegerOption(option =>
    option.setName('faces')
      .setDescription('Number of faces (2-100, default 6)')
      .setRequired(false)
      .setMinValue(2)
      .setMaxValue(100)
  );

export async function execute(interaction) {
  const guildId = interaction.guildId;
  const faces = interaction.options.getInteger('faces') || 6;
  const resultado = Math.floor(Math.random() * faces) + 1;

  const embed = new EmbedBuilder()
    .setTitle(MessagesService.get(guildId, 'Dados:Titulo'))
    .setDescription(MessagesService.get(guildId, 'Dados:Resultado', { resultado, caras: faces }))
    .setColor(0x9b59b6)
    .setFooter({ text: MessagesService.get(guildId, 'Dados:Pie', { usuario: interaction.user.username }) });

  await interaction.reply({ embeds: [embed] });
}
