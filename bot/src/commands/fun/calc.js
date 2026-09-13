import { EmbedBuilder } from 'discord.js';
import { evaluate } from 'mathjs';
import { slash } from '../../lib/slash.js';
import MessagesService from '../../services/messagesService.js';

export const data = slash('calc', 'Evaluate a mathematical expression')
    .addStringOption(o => o.setName('expression').setDescription('Math expression (e.g. 2+2, 5*5)').setRequired(true));

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const expression = interaction.options.getString('expression');

    try {
        const result = evaluate(expression);
        await interaction.reply({
            embeds: [
                new EmbedBuilder()
                    .setTitle(MessagesService.get(guildId, 'Calculadora:Titulo'))
                    .addFields(
                        { name: MessagesService.get(guildId, 'Calculadora:Expresion'), value: `\`${expression}\``, inline: true },
                        { name: MessagesService.get(guildId, 'Calculadora:Resultado'), value: `**${result}**`, inline: true }
                    )
                    .setColor(0x3498DB)
            ]
        });
    } catch {
        await interaction.reply({
            content: MessagesService.get(guildId, 'Calculadora:ErrorSintaxis'),
            ephemeral: true
        });
    }
}
