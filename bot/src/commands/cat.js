import { SlashCommandBuilder, EmbedBuilder } from 'discord.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
  .setName('cat')
  .setDescription('Show a random cat picture');

export async function execute(interaction) {
  const guildId = interaction.guildId;
  await interaction.deferReply();

  try {
    const response = await fetch('https://api.thecatapi.com/v1/images/search');
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`);
    }
    const apiData = await response.json();
    
    if (apiData && apiData.length > 0 && apiData[0].url) {
      const fotoUrl = apiData[0].url;
      const titulo = generarTituloMew();
      
      const embed = new EmbedBuilder()
        .setTitle(titulo)
        .setImage(fotoUrl)
        .setFooter({ text: fotoUrl })
        .setColor('#f9c2d1');
      
      await interaction.editReply({ embeds: [embed] });
    } else {
      await interaction.editReply(MessagesService.get(guildId, 'Gato:Error'));
    }
  } catch (error) {
    console.error('Error fetching cat image:', error);
    await interaction.editReply(MessagesService.get(guildId, 'Gato:Error'));
  }
}

function generarTituloMew() {
  let titulo = '';
  titulo += Math.random() < 0.5 ? 'M' : 'm';
  titulo += 'e'.repeat(Math.floor(Math.random() * 20) + 1);
  titulo += 'w'.repeat(Math.floor(Math.random() * 10) + 1);

  const exclamations = Math.floor(Math.random() * 11);
  const questions = Math.floor(Math.random() * 11);
  if (exclamations > 0) titulo += '!'.repeat(exclamations);
  if (questions > 0) titulo += '?'.repeat(questions);

  if (Math.random() < 0.5) {
    const treses = Math.floor(Math.random() * 5) + 1;
    titulo += ' :' + '3'.repeat(treses);
  }

  return titulo;
}
