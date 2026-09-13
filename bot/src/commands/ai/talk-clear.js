import { PermissionFlagsBits } from 'discord.js';
import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import { clearHistory } from '../../services/ai.js';
import MessagesService from '../../services/messagesService.js';

export const data = slash('talk-clear', "Reset the server's shared AI conversation", {
    names: esPt('charlar-limpiar', 'conversar-limpar'),
    descriptions: esPt('Reinicia la conversación compartida de la IA', 'Reinicia a conversa compartilhada da IA'),
    permissions: PermissionFlagsBits.ManageGuild
});

export async function execute(interaction) {
    const guildId = interaction.guildId;
    if (clearHistory(guildId)) {
        return interaction.reply(MessagesService.get(guildId, 'Chat:Limpiado'));
    }
    return interaction.reply({
        content: MessagesService.get(guildId, 'Chat:SinConversacion'),
        ephemeral: true
    });
}
