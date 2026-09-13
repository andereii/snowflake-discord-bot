import { EmbedBuilder } from 'discord.js';
import { askAi, registerGeneratedMessage, isGeneratedMessage, getGeneratedMessageGuild } from '../../services/ai.js';
import { createConfirmation } from '../../services/aiConfirmation.js';
import { formatAiFallbackNotice } from '../../services/fallbackNotices.js';
import { getGuildConfig, isEnabled } from '../../services/guildConfig.js';
import MessagesService from '../../services/messagesService.js';
import { PREFIX } from '../../lib/prefix.js';
import { applyMusicWidgetFromCommands } from '../../services/musicWidget.js';
import { maybeSpontaneousComment } from '../../services/aiSpontaneous.js';

export default async function aiHandler(message, client) {
    if (message.author.bot || !message.guildId) return;

    const text = message.content?.trim();
    if (!text || text.startsWith(PREFIX)) return;

    const referenced = message.reference?.messageId;
    if (referenced && isGeneratedMessage(referenced)
        && getGeneratedMessageGuild(referenced) === message.guildId) {
        await respond(message, client, text);
        return;
    }

    if (message.mentions.has(client.user.id)) {
        const cfg = getGuildConfig(message.guildId);
        if (cfg.AiMentionsEnabled !== 1) return;
        const clean = text.replace(new RegExp(`<@!?${client.user.id}>`, 'g'), '').trim();
        if (!clean) return;
        await respond(message, client, clean);
        return;
    }

    await maybeSpontaneousComment(message, client);
}

async function respond(message, client, text) {
    const guildId = message.guildId;
    const cfg = getGuildConfig(guildId);
    await message.channel.sendTyping();

    const ctx = { client, guild: message.guild, channel: message.channel, member: message.member };

    try {
        const outcome = await askAi(ctx, message.member?.displayName || message.author.username, text, {
            webSearchEnabled: isEnabled(cfg, 'AiWebSearchEnabled', true),
            commandsEnabled: isEnabled(cfg, 'AiCommandsEnabled', true),
            onFallback: async (info) => {
                await message.reply({
                    content: formatAiFallbackNotice(MessagesService.locale(guildId), info)
                }).catch(() => {});
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
        if (embeds.length) payload.embeds = embeds;

        const sent = await message.reply(payload);
        if (sent) registerGeneratedMessage(sent.id, guildId);
        await applyMusicWidgetFromCommands(message.channel, guildId, outcome.commands);
    } catch (error) {
        console.error('[aiHandler]', error);
        await message.reply(MessagesService.get(guildId, 'Chat:Error')).catch(() => {});
    }
}
