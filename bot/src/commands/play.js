import { SlashCommandBuilder, EmbedBuilder } from 'discord.js';
import { joinVoiceChannel } from '@discordjs/voice';
import { getQueue, createQueue, playNext, searchSong } from '../services/music.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
  .setName('play')
  .setDescription('Play a song, playlist, or attached file')
  .addStringOption(option => 
    option.setName('query')
      .setDescription('URL or search query')
      .setRequired(false)
  )
  .addAttachmentOption(option =>
    option.setName('file')
      .setDescription('Audio file to play')
      .setRequired(false)
  );

export async function execute(interaction) {
  const guildId = interaction.guildId;
  const query = interaction.options.getString('query');
  const attachment = interaction.options.getAttachment('file');
  const member = interaction.member;
  const voiceChannel = member.voice.channel;

  if (!voiceChannel) {
    return interaction.reply({ content: MessagesService.get(guildId, 'Musica:NoEnCanal'), ephemeral: true });
  }

  if (!query && !attachment) {
    return interaction.reply({ content: MessagesService.get(guildId, 'Musica:NoConsulta'), ephemeral: true });
  }

  await interaction.deferReply();

  let queue = getQueue(interaction.guildId);
  if (!queue) {
    queue = createQueue(interaction.guildId, voiceChannel, interaction.channel);
    queue.connection = joinVoiceChannel({
        channelId: voiceChannel.id,
        guildId: interaction.guildId,
        adapterCreator: interaction.guild.voiceAdapterCreator,
    });
    queue.connection.subscribe(queue.player);
  }

  let songInfo = null;
  if (attachment) {
      if (!attachment.contentType?.startsWith('audio/')) {
          return interaction.editReply({ content: MessagesService.get(guildId, 'Musica:ArchivoInvalido') });
      }
      songInfo = {
          title: attachment.name,
          url: attachment.url,
          duration: null,
          thumbnail: null,
          isAttachment: true
      };
  } else {
      songInfo = await searchSong(query);
  }

  if (!songInfo) {
    return interaction.editReply({ content: MessagesService.get(guildId, 'Musica:NoEncontrado') });
  }

  const song = {
      ...songInfo,
      requester: interaction.user.tag
  };

  queue.songs.push(song);

  if (!queue.playing) {
      await interaction.editReply({ content: MessagesService.get(guildId, 'Musica:Tocando', { titulo: song.title }) });
      playNext(interaction.guildId);
  } else {
      const embed = new EmbedBuilder()
          .setColor('Blurple')
          .setTitle(MessagesService.get(guildId, 'Musica:PuestaEnCola', { titulo: song.title }))
          .setDescription(`**[${song.title}](${song.url})**\n${interaction.user.tag}`);
      if (song.thumbnail) embed.setThumbnail(song.thumbnail);
      
      await interaction.editReply({ embeds: [embed] });
  }
}
