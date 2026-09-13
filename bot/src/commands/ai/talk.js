import { EmbedBuilder } from 'discord.js';
import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import { askAi, registerGeneratedMessage } from '../../services/ai.js';
import { applyMusicWidgetFromCommands } from '../../services/musicWidget.js';
import { createConfirmation } from '../../services/aiConfirmation.js';
import { formatAiFallbackNotice } from '../../services/fallbackNotices.js';
import { getGuildConfig, isEnabled } from '../../services/guildConfig.js';
import MessagesService from '../../services/messagesService.js';

export const data = slash('talk', "Talk to the AI in the server's shared conversation", {
    names: esPt('charlar', 'conversar'),
    descriptions: esPt(
        'Habla con la IA en la conversación compartida del servidor',
        'Converse com a IA na conversa compartilhada do servidor'
    )
}).addStringOption(option =>
    option.setName('text')
        .setDescription('What you want to say or ask')
        .setRequired(true)
);

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const locale = MessagesService.locale(guildId);
    const cfg = getGuildConfig(guildId);

    if (!isEnabled(cfg, 'AiChatEnabled', true)) {
        return interaction.reply({
            content: MessagesService.get(guildId, 'Chat:Desactivado', {}, { interaction }),
            ephemeral: true
        });
    }

    const text = interaction.options.getString('text');
    const userName = interaction.member?.displayName || interaction.user.username;
    const thinking = `> ${MessagesService.get(guildId, 'Chat:Pensando', {}, { interaction })}`;
    const searching = `> 🔍 ${MessagesService.get(guildId, 'Chat:BuscandoWeb', {}, { interaction })}`;

    await interaction.deferReply().catch(() => {});
    await interaction.editReply(thinking).catch(() => {});

    const ctx = {
        client: interaction.client,
        guild: interaction.guild,
        channel: interaction.channel,
        member: interaction.member
    };

    try {
        let searchShown = false;
        const outcome = await askAi(ctx, userName, text, {
            webSearchEnabled: isEnabled(cfg, 'AiWebSearchEnabled', true),
            commandsEnabled: isEnabled(cfg, 'AiCommandsEnabled', true),
            onSearching: async () => {
                if (searchShown) return;
                searchShown = true;
                await interaction.editReply(`${thinking}\n${searching}`).catch(() => {});
            },
            onFallback: async (info) => {
                await interaction.followUp({
                    content: formatAiFallbackNotice(locale, info),
                    ephemeral: true
                }).catch(() => {});
            }
        });

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

        let content = outcome.text || '';
        if (searchShown) content = `${searching}\n\n${content}`;

        const embeds = (outcome.commands || []).map(cmd =>
            new EmbedBuilder()
                .setTitle(cmd.description)
                .setDescription(cmd.text)
                .setColor(cmd.success ? 0x2ECC71 : 0xE74C3C)
        );

        const payload = { content: content || undefined };
        if (embeds.length) payload.embeds = embeds;

        const msg = await interaction.editReply(payload);
        if (msg) registerGeneratedMessage(msg.id, guildId);
        await applyMusicWidgetFromCommands(interaction.channel, guildId, outcome.commands);
    } catch (error) {
        console.error('[talk]', error);
        await interaction.editReply(MessagesService.get(guildId, 'Chat:Error', {}, { interaction })).catch(() => {});
    }
}
