import { SlashCommandBuilder, EmbedBuilder } from 'discord.js';
import { askAi, registerGeneratedMessage } from '../services/ai.js';
import { createConfirmation } from '../services/aiConfirmation.js';
import MessagesService from '../services/messagesService.js';
import { formatAiFallbackNotice } from '../services/fallbackNotices.js';
import db from '../services/database.js';

export const data = new SlashCommandBuilder()
    .setName('talk')
    .setDescription('Talk to the AI in the server\'s shared conversation')
    .addStringOption(option =>
        option.setName('text')
            .setDescription('What you want to say or ask')
            .setRequired(true)
    );

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const locale = MessagesService.locale(guildId);
    
    // Check if AI is enabled in guild settings
    const guildSettings = db.prepare(
        'SELECT AiChatEnabled, AiWebSearchEnabled, AiCommandsEnabled FROM GuildConfigs WHERE GuildId = ?'
    ).get(guildId);

    if (guildSettings && guildSettings.AiChatEnabled === 0) {
        return interaction.reply({
            content: MessagesService.get(guildId, 'Chat:Desactivado', {}, { interaction }),
            ephemeral: true
        });
    }

    const webSearchEnabled = !guildSettings || guildSettings.AiWebSearchEnabled !== 0;
    const commandsEnabled = !guildSettings || guildSettings.AiCommandsEnabled !== 0;

    const texto = interaction.options.getString('text');
    const userName = interaction.member?.displayName || interaction.user.username;

    const thinkingText = `> 🧠 ${MessagesService.get(guildId, 'Chat:Pensando', {}, { interaction })}`;
    const searchingText = `> 🔍 ${MessagesService.get(guildId, 'Chat:BuscandoWeb', {}, { interaction })}`;

    await interaction.deferReply().catch(() => {});
    await interaction.editReply(thinkingText).catch(() => {});

    const ctx = {
        client: interaction.client,
        guild: interaction.guild,
        channel: interaction.channel,
        member: interaction.member
    };

    try {
        let searchFeedbackSent = false;

        const outcome = await askAi(ctx, userName, texto, {
            webSearchEnabled,
            commandsEnabled,
            onSearching: async () => {
                if (!searchFeedbackSent) {
                    searchFeedbackSent = true;
                    await interaction.editReply(`${thinkingText}\n${searchingText}`).catch(() => {});
                }
            },
            onFallback: async (info) => {
                const notice = formatAiFallbackNotice(locale, info);
                await interaction.followUp({
                    content: notice,
                    ephemeral: true
                }).catch(() => {});
            }
        });

        // 1. If there is a destructive command pending confirmation
        if (outcome.pending) {
            await interaction.deleteReply().catch(() => {});

            await createConfirmation({
                ctx,
                toolName: outcome.pending.toolName,
                args: outcome.pending.args,
                callId: outcome.pending.callId,
                isEphemeral: true,
                interaction
            });
            return;
        }

        // 2. Normal text output + command embeds
        let finalContent = outcome.text || '';
        if (searchFeedbackSent) {
            finalContent = `${searchingText}\n\n${finalContent}`;
        }

        const embeds = (outcome.commands || []).map(cmd =>
            new EmbedBuilder()
                .setTitle(cmd.description)
                .setDescription(cmd.text)
                .setColor(cmd.success ? 0x2ECC71 : 0xE74C3C)
        );

        const replyPayload = { content: finalContent || undefined };
        if (embeds.length > 0) replyPayload.embeds = embeds;

        const msg = await interaction.editReply(replyPayload);

        if (msg) {
            registerGeneratedMessage(msg.id, guildId);
        }
    } catch (error) {
        console.error('[talk] Error:', error);
        await interaction.editReply(MessagesService.get(guildId, 'Chat:Error', {}, { interaction })).catch(() => {});
    }
}
