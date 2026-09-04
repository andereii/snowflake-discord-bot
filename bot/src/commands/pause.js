import { SlashCommandBuilder } from 'discord.js';
import { getQueue } from '../services/music.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
  .setName('pause')
  .setDescription('Pause the current song');

export async function execute(interaction) {
  const guildId = interaction.guildId;
  const queue = getQueue(guildId);
  if (!queue || !queue.playing) {
    return interaction.reply({ content: MessagesService.get(guildId, 'Musica:NoActivo'), ephemeral: true });
  }

  queue.player.pause();
  await interaction.reply(MessagesService.get(guildId, 'Musica:Pausado'));
}
