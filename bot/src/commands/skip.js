import { SlashCommandBuilder } from 'discord.js';
import { getQueue, playNext } from '../services/music.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
  .setName('skip')
  .setDescription('Skip the currently playing song');

export async function execute(interaction) {
  const guildId = interaction.guildId;
  const queue = getQueue(guildId);
  if (!queue || !queue.playing) {
    return interaction.reply({ content: MessagesService.get(guildId, 'Musica:NoActivo'), ephemeral: true });
  }

  if (queue.songs.length > 0) {
    const nextSong = queue.songs[0];
    await interaction.reply(MessagesService.get(guildId, 'Musica:SaltadoProxima', { titulo: nextSong.title }));
  } else {
    await interaction.reply(MessagesService.get(guildId, 'Musica:SaltadoVacio'));
  }
  
  playNext(guildId);
}
