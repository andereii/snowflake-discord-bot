import { SlashCommandBuilder } from 'discord.js';
import { getQueue, deleteQueue } from '../services/music.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
  .setName('stop')
  .setDescription('Stop music playback and clear the queue');

export async function execute(interaction) {
  const guildId = interaction.guildId;
  const queue = getQueue(guildId);
  if (!queue) {
    return interaction.reply({ content: MessagesService.get(guildId, 'Musica:NoActivo'), ephemeral: true });
  }

  queue.songs = [];
  queue.player.stop();
  deleteQueue(guildId);

  await interaction.reply(MessagesService.get(guildId, 'Musica:ReproduccionDetenida'));
}
