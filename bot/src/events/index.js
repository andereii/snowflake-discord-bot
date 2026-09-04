import { Events } from 'discord.js';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import { handleGuildMemberAdd } from './welcomeHandler.js';
import { isConfirmationInteraction, handleButtonInteraction } from '../services/aiConfirmation.js';
import { isImageWidgetInteraction, handleButtonInteraction as handleImageButtonInteraction } from '../services/imageSearchWidget.js';
import { isTriviaInteraction, handleTriviaButton } from '../services/triviaService.js';
import { initHardmuteScheduler } from '../services/hardmuteManager.js';
import { handleVoiceStateUpdate } from '../services/voiceHub.js';
import { startYouTubeNotifier } from '../services/youtubeService.js';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export async function registerEvents(client) {
  // Load message handlers
  const messageHandlers = [];
  const handlersPath = path.join(__dirname, 'messageHandlers');
  if (fs.existsSync(handlersPath)) {
    const files = fs.readdirSync(handlersPath).filter(f => f.endsWith('.js'));
    for (const file of files) {
      const handler = await import(path.join(handlersPath, file));
      if (handler.default) messageHandlers.push(handler.default);
    }
  }

  client.on(Events.ClientReady, () => {
    console.log(`[bot] Sesión iniciada como ${client.user.tag}`);
    initHardmuteScheduler(client);
    startYouTubeNotifier(client);
  });

  client.on(Events.GuildCreate, (guild) => {
    console.log(`[bot] Servidor añadido: ${guild.name} (${guild.id})`);
  });

  client.on(Events.GuildDelete, (guild) => {
    console.log(`[bot] Servidor eliminado: ${guild.name} (${guild.id})`);
  });

  client.on(Events.GuildMemberAdd, async (member) => {
    await handleGuildMemberAdd(member, client);
  });

  client.on(Events.VoiceStateUpdate, async (oldState, newState) => {
    await handleVoiceStateUpdate(oldState, newState);
  });

  client.on(Events.MessageReactionAdd, async (reaction, user) => {
    if (user.bot) return;
    if (reaction.partial) await reaction.fetch();
    const { activePolls } = await import("../commands/poll.js");
    const poll = activePolls.get(reaction.message.id);
    if (poll && !poll.multiOpcion) {
      const voters = poll.voters;
      if (voters.has(user.id)) {
        await reaction.users.remove(user.id).catch(() => {});
      } else {
        voters.set(user.id, reaction.emoji.name);
      }
    }
  });

  client.on(Events.MessageReactionRemove, async (reaction, user) => {
    if (user.bot) return;
    const { activePolls } = await import("../commands/poll.js");
    const poll = activePolls.get(reaction.message.id);
    if (poll && !poll.multiOpcion) {
      const voters = poll.voters;
      if (voters.get(user.id) === reaction.emoji.name) {
        voters.delete(user.id);
      }
    }
  });

  client.on(Events.MessageCreate, async (message) => {
    if (message.author.bot) return;
    for (const handler of messageHandlers) {
      try {
        await handler(message, client);
      } catch (err) {
        console.error('[bot] Error en messageHandler:', err);
      }
    }
  });

  client.on(Events.InteractionCreate, async (interaction) => {
    // 1. Button interactions (AI destructive command confirmations, polls, etc.)
    if (interaction.isButton()) {
      if (isConfirmationInteraction(interaction.customId)) {
        await handleButtonInteraction(interaction);
        return;
      }
      if (isImageWidgetInteraction(interaction.customId)) {
        await handleImageButtonInteraction(interaction);
        return;
      }
      if (isTriviaInteraction(interaction.customId)) {
        await handleTriviaButton(interaction);
        return;
      }
    }

    // 2. Slash command interactions
    if (interaction.isChatInputCommand()) {
      const command = interaction.client.commands.get(interaction.commandName);
      if (!command) return;
      try {
        await command.execute(interaction);
      } catch (error) {
        const reply = { content: 'Hubo un error al ejecutar este comando.', ephemeral: true };
        try {
          if (interaction.replied || interaction.deferred) {
            await interaction.followUp(reply).catch(() => {});
          } else {
            await interaction.reply(reply).catch(() => {});
          }
        } catch {}
      }
    }
  });
}
