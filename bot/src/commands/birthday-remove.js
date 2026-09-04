import { SlashCommandBuilder } from 'discord.js';
import db from '../services/database.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
  .setName('birthday-remove')
  .setDescription('Delete your registered birthday from this server');

export async function execute(interaction) {
  const guildId = interaction.guildId;
  const result = db.prepare('DELETE FROM Birthdays WHERE GuildId = ? AND UserId = ?')
    .run(guildId, interaction.user.id);

  if (result.changes > 0) {
    await interaction.reply({
      content: MessagesService.get(guildId, 'Cumple:Quitado'),
      ephemeral: true
    });
  } else {
    await interaction.reply({
      content: MessagesService.get(guildId, 'Cumple:NoRegistrado'),
      ephemeral: true
    });
  }
}
