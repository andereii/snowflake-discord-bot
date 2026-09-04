import { SlashCommandBuilder, AttachmentBuilder } from 'discord.js';
import { spawn } from 'child_process';
import fs from 'fs';
import path from 'path';
import MessagesService from '../services/messagesService.js';

export const activePolls = new Map();

const NUMBERS = ['1️⃣', '2️⃣', '3️⃣', '4️⃣', '5️⃣', '6️⃣', '7️⃣', '8️⃣', '9️⃣', '🔟'];

export const data = new SlashCommandBuilder()
  .setName('poll')
  .setDescription('Create an interactive poll')
  .addStringOption(option => option.setName('question').setDescription('The poll question').setRequired(true))
  .addStringOption(option => option.setName('options').setDescription('Comma-separated options').setRequired(true))
  .addIntegerOption(option => option.setName('minutes').setDescription('Duration in minutes'))
  .addBooleanOption(option => option.setName('multiple').setDescription('Allow multiple selections'));

export async function execute(interaction) {
  const guildId = interaction.guildId;
  const pregunta = interaction.options.getString('question');
  const opcionesStr = interaction.options.getString('options');
  const minutos = interaction.options.getInteger('minutes') || 0;
  const multiOpcion = interaction.options.getBoolean('multiple') ?? false;

  const opcionesRaw = opcionesStr.split(',').map(o => o.trim()).filter(o => o.length > 0);
  if (opcionesRaw.length < 2) {
    return interaction.reply({ content: MessagesService.get(guildId, 'Encuestas:ErrorMinOpciones'), ephemeral: true });
  }
  if (opcionesRaw.length > 10) {
    return interaction.reply({ content: MessagesService.get(guildId, 'Encuestas:ErrorMaxOpciones'), ephemeral: true });
  }

  const opciones = opcionesRaw.map((texto, i) => ({ id: i, emoji: NUMBERS[i], texto, votos: 0 }));

  const opcionesHeader = MessagesService.get(guildId, 'Encuestas:Opciones');
  let desc = `**${opcionesHeader}:**\n\n` + opciones.map(o => `${o.emoji} ${o.texto}`).join('\n');
  if (minutos > 0) {
    const timestamp = Math.floor(Date.now() / 1000) + minutos * 60;
    desc += `\n\n⏳ <t:${timestamp}:R>`;
  }

  const multiLabel = multiOpcion ? MessagesService.get(guildId, 'Encuestas:MultiOpcionLabel') : '1 voto';

  const embed = {
    title: pregunta,
    description: desc,
    color: 0x3498db,
    footer: { text: `${interaction.user.tag} | ${multiLabel}` }
  };

  const message = await interaction.reply({ embeds: [embed], fetchReply: true });

  for (const op of opciones) {
    await message.react(op.emoji);
  }

  const pollId = message.id;
  activePolls.set(pollId, {
    id: pollId,
    guildId,
    pregunta,
    opciones,
    multiOpcion,
    authorId: interaction.user.id,
    voters: new Map()
  });

  if (minutos > 0) {
    setTimeout(() => endPoll(message, interaction.client), minutos * 60 * 1000);
  }
}

export async function endPoll(message, client) {
  const poll = activePolls.get(message.id);
  if (!poll) return;
  activePolls.delete(message.id);

  const fetchedMsg = await message.channel.messages.fetch(message.id).catch(() => null);
  if (!fetchedMsg) return;

  for (const op of poll.opciones) {
    const reaction = fetchedMsg.reactions.cache.get(op.emoji);
    if (reaction) {
      await reaction.users.fetch();
      const count = reaction.users.cache.filter(u => u.id !== client.user.id).size;
      op.votos = count;
    }
  }

  const inputJson = JSON.stringify({
    title: poll.pregunta,
    options: poll.opciones.map(o => ({ label: o.texto, count: o.votos }))
  });

  const binPath = path.resolve(process.cwd(), '../src/Dlang/piechart');
  const outPath = path.resolve(process.cwd(), `../data/poll_${poll.id}.png`);

  try {
    const proc = spawn(binPath, [outPath]);
    proc.stdin.write(inputJson);
    proc.stdin.end();

    await new Promise((resolve, reject) => {
      proc.on('close', code => {
        if (code === 0) resolve();
        else reject(new Error(`Binary exited with code ${code}`));
      });
      proc.on('error', reject);
    });

    const attachment = new AttachmentBuilder(outPath, { name: 'piechart.png' });
    const resultEmbed = {
      title: poll.pregunta,
      description: MessagesService.get(poll.guildId, 'Encuestas:FinalizadaDesc'),
      color: 0x2ecc71,
      image: { url: 'attachment://piechart.png' }
    };

    await fetchedMsg.edit({ embeds: [resultEmbed], files: [attachment] });
    fs.unlink(outPath, () => {});
  } catch (err) {
    console.error('[bot] Error generating piechart:', err);
    await fetchedMsg.edit({ content: MessagesService.get(poll.guildId, 'Encuestas:FinalizadaDesc') });
  }
}
