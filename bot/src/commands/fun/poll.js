import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import { activePolls, NUMBER_EMOJIS, registerPoll, endPoll } from '../../services/poll.js';
import MessagesService from '../../services/messagesService.js';

export { activePolls };

export const data = slash('poll', 'Create an interactive poll', {
    names: esPt('encuesta', 'enquete'),
    descriptions: esPt('Crea una encuesta interactiva', 'Cria uma enquete interativa')
})
    .addStringOption(o => o.setName('question').setDescription('The poll question').setRequired(true))
    .addStringOption(o => o.setName('options').setDescription('Comma-separated options').setRequired(true))
    .addIntegerOption(o => o.setName('minutes').setDescription('Duration in minutes'))
    .addBooleanOption(o => o.setName('multiple').setDescription('Allow multiple selections'));

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const question = interaction.options.getString('question');
    const raw = interaction.options.getString('options');
    const minutes = interaction.options.getInteger('minutes') || 0;
    const multiVote = interaction.options.getBoolean('multiple') ?? false;

    const texts = raw.split(',').map(o => o.trim()).filter(Boolean);
    if (texts.length < 2) {
        return interaction.reply({ content: MessagesService.get(guildId, 'Encuestas:ErrorMinOpciones'), ephemeral: true });
    }
    if (texts.length > 10) {
        return interaction.reply({ content: MessagesService.get(guildId, 'Encuestas:ErrorMaxOpciones'), ephemeral: true });
    }

    const options = texts.map((text, i) => ({ id: i, emoji: NUMBER_EMOJIS[i], text, votes: 0 }));
    const header = MessagesService.get(guildId, 'Encuestas:Opciones');
    let description = `**${header}:**\n\n` + options.map(o => `${o.emoji} ${o.text}`).join('\n');
    if (minutes > 0) {
        description += `\n\n⏳ <t:${Math.floor(Date.now() / 1000) + minutes * 60}:R>`;
    }

    const multiLabel = multiVote ? MessagesService.get(guildId, 'Encuestas:MultiOpcionLabel') : '1 voto';
    const message = await interaction.reply({
        embeds: [{
            title: question,
            description,
            color: 0x3498db,
            footer: { text: `${interaction.user.tag} | ${multiLabel}` }
        }],
        fetchReply: true
    });

    for (const option of options) await message.react(option.emoji);

    registerPoll(message.id, {
        id: message.id,
        guildId,
        question,
        options,
        multiVote,
        authorId: interaction.user.id,
        voters: new Map()
    });

    if (minutes > 0) {
        setTimeout(() => endPoll(message, interaction.client), minutes * 60 * 1000);
    }
}
