import { SlashCommandBuilder } from 'discord.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
  .setName('ping')
  .setDescription('Responde con el ping del bot');

export async function execute(interaction) {
  const sent = await interaction.reply({ content: '...', fetchReply: true });
  const latency = sent.createdTimestamp - interaction.createdTimestamp;
  await interaction.editReply(MessagesService.get(interaction.guildId, 'Ping:Respuesta', { latencia: latency }));
}
