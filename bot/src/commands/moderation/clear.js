import { PermissionFlagsBits } from 'discord.js';
import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import MessagesService from '../../services/messagesService.js';

export const data = slash('clear', 'Delete a specified number of messages from the channel', {
    names: esPt('limpiar', 'limpar'),
    descriptions: esPt('Elimina una cantidad de mensajes del canal', 'Limpa uma quantidade de mensagens do canal'),
    permissions: PermissionFlagsBits.ManageMessages
}).addIntegerOption(o =>
    o.setName('amount').setDescription('Number of messages to delete (1-100)').setRequired(true).setMinValue(1).setMaxValue(100)
);

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const amount = interaction.options.getInteger('amount');
    try {
        const deleted = await interaction.channel.bulkDelete(amount, true);
        await interaction.reply({
            content: MessagesService.get(guildId, 'Limpiar:Exito', { borrados: deleted.size, pedidos: amount }),
            ephemeral: true
        });
    } catch (error) {
        console.error('[clear]', error);
        await interaction.reply({ content: MessagesService.get(guildId, 'Errores:Interno'), ephemeral: true });
    }
}
