import { SlashCommandBuilder, PermissionFlagsBits } from 'discord.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
    .setName('clear')
    .setDescription('Delete a specified number of messages from the channel')
    .addIntegerOption(option =>
        option.setName('amount')
            .setDescription('Number of messages to delete (1-100)')
            .setRequired(true)
            .setMinValue(1)
            .setMaxValue(100)
    )
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageMessages);

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const cantidad = interaction.options.getInteger('amount');

    try {
        const deleted = await interaction.channel.bulkDelete(cantidad, true);
        const exito = MessagesService.get(guildId, 'Limpiar:Exito', { borrados: deleted.size, pedidos: cantidad });
        await interaction.reply({ content: exito, ephemeral: true });
    } catch (error) {
        console.error(error);
        await interaction.reply({ content: MessagesService.get(guildId, 'Errores:Interno'), ephemeral: true });
    }
}
