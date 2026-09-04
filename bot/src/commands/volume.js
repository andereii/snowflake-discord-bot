import { SlashCommandBuilder } from 'discord.js';
import db from '../services/database.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
  .setName('volume')
  .setDescription('Set the music playback volume (0-100)')
  .addIntegerOption(option => 
    option.setName('level')
      .setDescription('Volume level (0-100)')
      .setRequired(true)
      .setMinValue(0)
      .setMaxValue(100)
  );

export async function execute(interaction) {
  const guildId = interaction.guildId;
  const level = interaction.options.getInteger('level');

  db.prepare('INSERT OR IGNORE INTO GuildConfigs (GuildId) VALUES (?)').run(guildId);
  db.prepare('UPDATE GuildConfigs SET Volume = ? WHERE GuildId = ?').run(level, guildId);

  await interaction.reply(MessagesService.get(guildId, 'Musica:Volumen', { volumen: level }));
}
