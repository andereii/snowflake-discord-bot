import { askAi, registerGeneratedMessage, isGeneratedMessage, getGeneratedMessageGuild } from '../../services/ai.js';
import { createConfirmation } from '../../services/aiConfirmation.js';
import { formatAiFallbackNotice } from '../../services/fallbackNotices.js';
import MessagesService from '../../services/messagesService.js';
import { EmbedBuilder } from 'discord.js';
import db from '../../services/database.js';

export default async function aiHandler(message, client) {
    if (message.author.bot) return;
    if (!message.guildId) return;

    const text = message.content?.trim();
    if (!text) return;

    // Ignore prefix commands so they don't trigger AI handler AND prefix handler simultaneously
    if (text.startsWith(';')) return;

    // Path 1: Reply to an AI-generated message
    const referenced = message.reference?.messageId;
    if (referenced && isGeneratedMessage(referenced)) {
        const guildId = getGeneratedMessageGuild(referenced);
        if (guildId === message.guildId) {
            await respondToAi(message, client, text);
            return;
        }
    }

    // Path 2: Bot is mentioned with @
    if (message.mentions.has(client.user.id)) {
        const guildSettings = db.prepare('SELECT AiMentionsEnabled FROM GuildConfigs WHERE GuildId = ?').get(message.guildId);
        
        if (!guildSettings || guildSettings.AiMentionsEnabled !== 1) return;

        const cleanText = text
            .replace(new RegExp(`<@!?${client.user.id}>`, 'g'), '')
            .trim();

        if (!cleanText) return;

        await respondToAi(message, client, cleanText);
        return;
    }
}

async function respondToAi(message, client, text) {
    const userName = message.member?.displayName || message.author.username;
    const guildId = message.guildId;

    const guildSettings = db.prepare(
        'SELECT AiWebSearchEnabled, AiCommandsEnabled FROM GuildConfigs WHERE GuildId = ?'
    ).get(guildId);

    const webSearchEnabled = !guildSettings || guildSettings.AiWebSearchEnabled !== 0;
    const commandsEnabled = !guildSettings || guildSettings.AiCommandsEnabled !== 0;

    await message.channel.sendTyping();

    const ctx = {
        client,
        guild: message.guild,
        channel: message.channel,
        member: message.member
    };

    try {
        const locale = MessagesService.locale(guildId);
        const outcome = await askAi(ctx, userName, text, {
            webSearchEnabled,
            commandsEnabled,
            onFallback: async (info) => {
                const notice = formatAiFallbackNotice(locale, info);
                await message.reply({ content: notice }).catch(() => {});
            }
        });

        if (outcome.pending) {
            await createConfirmation({
                ctx,
                toolName: outcome.pending.toolName,
                args: outcome.pending.args,
                callId: outcome.pending.callId,
                isEphemeral: false
            });
            return;
        }

        const embeds = (outcome.commands || []).map(cmd =>
            new EmbedBuilder()
                .setTitle(cmd.description)
                .setDescription(cmd.text)
                .setColor(cmd.success ? 0x2ECC71 : 0xE74C3C)
        );

        const payload = { content: outcome.text || undefined };
        if (embeds.length > 0) payload.embeds = embeds;

        const sent = await message.reply(payload);
        if (sent) {
            registerGeneratedMessage(sent.id, guildId);
        }
    } catch (error) {
        console.error('[aiHandler] Error responding:', error);
        await message.reply('Hubo un error al contactar a la IA.').catch(() => {});
    }
}
