import { SlashCommandBuilder, EmbedBuilder } from 'discord.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
  .setName('calc')
  .setDescription('Evaluate a mathematical expression')
  .addStringOption(option =>
    option.setName('expression')
      .setDescription('Math expression (e.g. 2+2, 5*5)')
      .setRequired(true)
  );

export async function execute(interaction) {
  const guildId = interaction.guildId;
  const expresion = interaction.options.getString('expression');
  try {
    const sanitized = expresion.replace(/[^0-9+\-*/(). ]/g, '');
    const result = new Function(`return ${sanitized}`)();

    const embed = new EmbedBuilder()
      .setTitle(MessagesService.get(guildId, 'Calculadora:Titulo'))
      .addFields(
        { name: MessagesService.get(guildId, 'Calculadora:Expresion'), value: `\`${sanitized}\``, inline: true },
        { name: MessagesService.get(guildId, 'Calculadora:Resultado'), value: `**${result}**`, inline: true }
      )
      .setColor(0x3498DB);

    await interaction.reply({ embeds: [embed] });
  } catch {
    await interaction.reply({
      content: MessagesService.get(guildId, 'Calculadora:ErrorSintaxis'),
      ephemeral: true
    });
  }
}
