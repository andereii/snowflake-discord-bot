import { SlashCommandBuilder } from 'discord.js';

const RESPUESTAS_POSITIVAS = [
  'Es cierto.', 'Definitivamente sí.', 'Sin duda.', 'Sí, seguro.',
  'Puedes contar con ello.', 'En mi opinión, sí.', 'Probablemente.',
  'El universo dice que sí.', 'Las señales apuntan a que sí.',
  'Todo apunta a que sí.'
];

const RESPUESTAS_NEUTRALES = [
  'Concéntrate y pregunta de nuevo.', 'Mejor no te lo digo ahora.',
  'No puedo predecirlo ahora.', 'Pregunta de nuevo más tarde.',
  'Es complicado, vuelve a intentarlo.', 'No tengo una respuesta clara para eso.',
  'Los astros no se deciden todavía.'
];

const RESPUESTAS_NEGATIVAS = [
  'No cuentes con ello.', 'Definitivamente no.', 'Mis fuentes dicen que no.',
  'Muy dudoso.', 'Lo dudo mucho.', 'No.', 'El universo dice que no.',
  'No es buena idea.', 'Las señales apuntan a que no.'
];

export const data = new SlashCommandBuilder()
  .setName('8ball')
  .setDescription('Hazle una pregunta a la bola mágica 8')
  .addStringOption(option =>
    option.setName('question')
      .setDescription('La pregunta que le harás a la bola 8')
      .setRequired(true)
  );

export async function execute(interaction) {
  const pregunta = interaction.options.getString('question');
  
  if (pregunta.trim().length < 3) {
    return await interaction.reply({
      content: 'Por favor, haz una pregunta real (mínimo 3 caracteres).',
      ephemeral: true
    });
  }

  const dado = Math.floor(Math.random() * 10);
  let respuesta, color;
  
  if (dado < 5) {
    respuesta = RESPUESTAS_POSITIVAS[Math.floor(Math.random() * RESPUESTAS_POSITIVAS.length)];
    color = 0x2ecc71;
  } else if (dado < 8) {
    respuesta = RESPUESTAS_NEUTRALES[Math.floor(Math.random() * RESPUESTAS_NEUTRALES.length)];
    color = 0xf1c40f;
  } else {
    respuesta = RESPUESTAS_NEGATIVAS[Math.floor(Math.random() * RESPUESTAS_NEGATIVAS.length)];
    color = 0xe74c3c;
  }

  await interaction.reply({
    embeds: [{
      title: '🎱 Bola Mágica 8',
      description: `🎱 **${respuesta}**`,
      color: color,
      fields: [{ name: 'Tu pregunta', value: `"${pregunta}"` }],
      footer: { text: `Preguntado por ${interaction.user.username}` }
    }]
  });
}
