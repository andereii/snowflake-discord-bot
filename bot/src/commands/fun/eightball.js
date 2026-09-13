import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import MessagesService from '../../services/messagesService.js';

const ANSWERS = {
    en: {
        yes: ['It is certain.', 'Definitely yes.', 'Without a doubt.', 'Yes, definitely.', 'You may rely on it.', 'As I see it, yes.', 'Most likely.', 'The universe says yes.', 'Signs point to yes.', 'Outlook good.'],
        maybe: ['Concentrate and ask again.', 'Better not tell you now.', 'Cannot predict now.', 'Ask again later.', "It's complicated, try again.", "I don't have a clear answer.", 'The stars have not decided yet.'],
        no: ["Don't count on it.", 'My reply is no.', 'My sources say no.', 'Very doubtful.', 'I doubt it.', 'No.', 'The universe says no.', 'Not a good idea.', 'Signs point to no.']
    },
    es: {
        yes: ['Es cierto.', 'Definitivamente sí.', 'Sin duda.', 'Sí, seguro.', 'Puedes contar con ello.', 'En mi opinión, sí.', 'Probablemente.', 'El universo dice que sí.', 'Las señales apuntan a que sí.', 'Todo apunta a que sí.'],
        maybe: ['Concéntrate y pregunta de nuevo.', 'Mejor no te lo digo ahora.', 'No puedo predecirlo ahora.', 'Pregunta de nuevo más tarde.', 'Es complicado, vuelve a intentarlo.', 'No tengo una respuesta clara para eso.', 'Los astros no se deciden todavía.'],
        no: ['No cuentes con ello.', 'Definitivamente no.', 'Mis fuentes dicen que no.', 'Muy dudoso.', 'Lo dudo mucho.', 'No.', 'El universo dice que no.', 'No es buena idea.', 'Las señales apuntan a que no.']
    },
    pt: {
        yes: ['É certo.', 'Definitivamente sim.', 'Sem dúvida.', 'Sim, com certeza.', 'Pode contar com isso.', 'Na minha opinião, sim.', 'Provavelmente.', 'O universo diz que sim.', 'Os sinais apontam que sim.', 'Tudo indica que sim.'],
        maybe: ['Concentre-se e pergunte de novo.', 'Melhor não te dizer agora.', 'Não consigo prever agora.', 'Pergunte novamente mais tarde.', 'É complicado, tente de novo.', 'Não tenho uma resposta clara.', 'Os astros ainda não se decidiram.'],
        no: ['Não conte com isso.', 'Definitivamente não.', 'Minhas fontes dizem que não.', 'Muito duvidoso.', 'Duvido muito.', 'Não.', 'O universo diz que não.', 'Não é uma boa ideia.', 'Os sinais apontam que não.']
    }
};

export const data = slash('8ball', 'Ask the magic 8-ball a question', {
    names: esPt('bola8'),
    descriptions: esPt('Hazle una pregunta a la bola mágica 8', 'Faça uma pergunta à bola mágica 8')
}).addStringOption(o => o.setName('question').setDescription('The question to ask').setRequired(true));

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const question = interaction.options.getString('question');

    if (question.trim().length < 3) {
        return interaction.reply({
            content: MessagesService.get(guildId, 'Bola8:ErrorPregunta'),
            ephemeral: true
        });
    }

    const locale = MessagesService.locale(guildId);
    const pack = ANSWERS[locale] || ANSWERS.en;
    const roll = Math.floor(Math.random() * 10);
    const bucket = roll < 5 ? 'yes' : roll < 8 ? 'maybe' : 'no';
    const color = bucket === 'yes' ? 0x2ecc71 : bucket === 'maybe' ? 0xf1c40f : 0xe74c3c;
    const answer = pack[bucket][Math.floor(Math.random() * pack[bucket].length)];

    await interaction.reply({
        embeds: [{
            title: MessagesService.get(guildId, 'Bola8:Titulo'),
            description: `🎱 **${answer}**`,
            color,
            fields: [{ name: MessagesService.get(guildId, 'Bola8:TuPregunta'), value: `"${question}"` }],
            footer: { text: MessagesService.get(guildId, 'Bola8:Pie', { autor: interaction.user.username }) }
        }]
    });
}
