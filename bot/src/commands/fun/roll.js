import { EmbedBuilder } from 'discord.js';
import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import MessagesService from '../../services/messagesService.js';

export const data = slash('roll', 'Roll a die', {
    names: esPt('dado'),
    descriptions: esPt('Lanza un dado de N caras', 'Rola um dado de N lados')
}).addIntegerOption(o =>
    o.setName('faces').setDescription('Number of faces (2-100, default 6)').setMinValue(2).setMaxValue(100)
);

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const faces = interaction.options.getInteger('faces') || 6;
    const result = Math.floor(Math.random() * faces) + 1;

    await interaction.reply({
        embeds: [
            new EmbedBuilder()
                .setTitle(MessagesService.get(guildId, 'Dados:Titulo'))
                .setDescription(MessagesService.get(guildId, 'Dados:Resultado', { resultado: result, caras: faces }))
                .setColor(0x9b59b6)
                .setFooter({ text: MessagesService.get(guildId, 'Dados:Pie', { usuario: interaction.user.username }) })
        ]
    });
}
