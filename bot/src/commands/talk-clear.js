import { SlashCommandBuilder, PermissionFlagsBits } from 'discord.js';
import { clearHistory } from '../services/ai.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
    .setName('talk-clear')
    .setDescription('Reset the server\'s shared AI conversation')
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageGuild);

export async function execute(interaction) {
    const guildId = interaction.guildId;
    if (clearHistory(guildId)) {
        await interaction.reply(MessagesService.get(guildId, 'Chat:Limpiado'));
    } else {
        await interaction.reply({
            content: MessagesService.get(guildId, 'Chat:SinConversacion'),
            ephemeral: true
        });
    }
}
