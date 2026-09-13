import { Events } from 'discord.js';
import { handleGuildMemberAdd } from './welcome.js';
import { isConfirmationInteraction, handleButtonInteraction } from '../services/aiConfirmation.js';
import { isImageWidgetInteraction, handleButtonInteraction as handleImageButton } from '../services/imageSearchWidget.js';
import { isMusicWidgetInteraction, handleMusicButton } from '../services/musicWidget.js';
import { COLOR_SELECT_ID, handleColorSelect } from '../services/colorService.js';
import { isTriviaInteraction, handleTriviaButton } from '../services/triviaService.js';
import { initHardmuteScheduler } from '../services/hardmuteManager.js';
import { initTimedRoleScheduler } from '../services/timedRoles.js';
import { handleVoiceStateUpdate } from '../services/voiceHub.js';
import { startYouTubeNotifier } from '../services/youtubeService.js';
import { getPoll, handlePollReactionAdd, handlePollReactionRemove } from '../services/poll.js';
import MessagesService from '../services/messagesService.js';
import afkHandler from './messages/afk.js';
import aiHandler from './messages/ai.js';
import countingHandler from './messages/counting.js';
import prefixHandler from './messages/prefix.js';

const messageHandlers = [prefixHandler, countingHandler, afkHandler, aiHandler];

const buttonHandlers = [
    [isConfirmationInteraction, handleButtonInteraction],
    [isImageWidgetInteraction, handleImageButton],
    [isTriviaInteraction, handleTriviaButton],
    [isMusicWidgetInteraction, handleMusicButton]
];

export function registerEvents(client) {
    client.on(Events.ClientReady, () => {
        console.log(`[bot] Logged in as ${client.user.tag}`);
        initHardmuteScheduler(client);
        initTimedRoleScheduler(client);
        startYouTubeNotifier(client);
    });

    client.on(Events.GuildCreate, (guild) => {
        console.log(`[bot] Joined guild: ${guild.name} (${guild.id})`);
    });
    client.on(Events.GuildDelete, (guild) => {
        console.log(`[bot] Left guild: ${guild.name} (${guild.id})`);
    });
    client.on(Events.GuildMemberAdd, (member) => handleGuildMemberAdd(member));
    client.on(Events.VoiceStateUpdate, (oldState, newState) => handleVoiceStateUpdate(oldState, newState));

    client.on(Events.MessageReactionAdd, async (reaction, user) => {
        if (user.bot) return;
        if (reaction.partial) await reaction.fetch();
        const poll = getPoll(reaction.message.id);
        handlePollReactionAdd(poll, user.id, reaction.emoji.name, () => reaction.users.remove(user.id));
    });

    client.on(Events.MessageReactionRemove, async (reaction, user) => {
        if (user.bot) return;
        handlePollReactionRemove(getPoll(reaction.message.id), user.id, reaction.emoji.name);
    });

    client.on(Events.MessageCreate, async (message) => {
        if (message.author.bot) return;
        for (const handler of messageHandlers) {
            try {
                await handler(message, client);
            } catch (err) {
                console.error('[bot] message handler:', err);
            }
        }
    });

    client.on(Events.InteractionCreate, async (interaction) => {
        if (interaction.isButton()) {
            for (const [match, handle] of buttonHandlers) {
                if (match(interaction.customId)) {
                    await handle(interaction);
                    return;
                }
            }
        }

        if (interaction.isStringSelectMenu() && interaction.customId === COLOR_SELECT_ID) {
            await handleColorSelect(interaction);
            return;
        }

        if (!interaction.isChatInputCommand()) return;
        const command = interaction.client.commands.get(interaction.commandName);
        if (!command) return;

        try {
            await command.execute(interaction);
        } catch (error) {
            console.error(`[bot] /${interaction.commandName}:`, error);
            const reply = {
                content: MessagesService.get(interaction.guildId, 'Errores:Interno'),
                ephemeral: true
            };
            if (interaction.replied || interaction.deferred) {
                await interaction.followUp(reply).catch(() => {});
            } else {
                await interaction.reply(reply).catch(() => {});
            }
        }
    });
}
