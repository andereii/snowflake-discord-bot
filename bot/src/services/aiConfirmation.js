import { ActionRowBuilder, ButtonBuilder, ButtonStyle, EmbedBuilder } from 'discord.js';
import { getToolByName } from './aiTools.js';
import { resumeAiTool } from './ai.js';
import MessagesService from './messagesService.js';
import crypto from 'crypto';

export const PREFIX_CUSTOM_ID = 'snowflake_ai_confirm_';
const TIMEOUT_MS = 15000;

/**
 * Maps token -> PendingState
 * @type {Map<string, {
 *   token: string,
 *   toolName: string,
 *   args: object,
 *   callId: string,
 *   commandDescription: string,
 *   ctx: { client: any, guild: any, channel: any, member: any },
 *   userId: string,
 *   timeoutId: any,
 *   interaction?: any,
 *   message?: any,
 *   isEphemeral: boolean
 * }>}
 */
const pendingConfirmations = new Map();

export function isConfirmationInteraction(customId) {
    return customId?.startsWith(PREFIX_CUSTOM_ID);
}

export async function createConfirmation({ ctx, toolName, args, callId, isEphemeral = false, interaction = null }) {
    const guildId = ctx.guild.id;
    const tool = getToolByName(toolName);
    const token = crypto.randomBytes(8).toString('hex');
    const commandDescription = tool?.describe ? await tool.describe(ctx, args) : toolName;

    const expireTimestamp = Math.floor((Date.now() + TIMEOUT_MS) / 1000);

    const embed = new EmbedBuilder()
        .setTitle(MessagesService.get(guildId, 'Chat:ConfirmacionTitulo'))
        .setDescription(
            `${MessagesService.get(guildId, 'Chat:ConfirmacionTexto', { comando: commandDescription })}\n\n⏱️ <t:${expireTimestamp}:R>`
        )
        .setFooter({ text: '15s' })
        .setColor(0xF1C40F);

    const row = new ActionRowBuilder().addComponents(
        new ButtonBuilder()
            .setCustomId(`${PREFIX_CUSTOM_ID}${token}_ok`)
            .setLabel(MessagesService.get(guildId, 'Chat:ConfirmacionAceptar'))
            .setStyle(ButtonStyle.Success),
        new ButtonBuilder()
            .setCustomId(`${PREFIX_CUSTOM_ID}${token}_no`)
            .setLabel(MessagesService.get(guildId, 'Chat:ConfirmacionRechazar'))
            .setStyle(ButtonStyle.Danger)
    );

    const state = {
        token,
        toolName,
        args,
        callId,
        commandDescription,
        ctx,
        userId: ctx.member.id,
        isEphemeral,
        interaction,
        message: null,
        timeoutId: null
    };

    state.timeoutId = setTimeout(() => handleTimeout(token), TIMEOUT_MS);
    pendingConfirmations.set(token, state);

    if (isEphemeral && interaction) {
        const msg = await interaction.followUp({
            embeds: [embed],
            components: [row],
            ephemeral: true
        });
        state.message = msg;
    } else {
        const msg = await ctx.channel.send({
            content: `<@${ctx.member.id}>`,
            embeds: [embed],
            components: [row]
        });
        state.message = msg;
    }

    return { token, commandDescription };
}

async function handleTimeout(token) {
    const state = pendingConfirmations.get(token);
    if (!state) return;
    pendingConfirmations.delete(token);

    await disableButtons(state);
    await resumeAiTool(state.ctx, {
        callId: state.callId,
        toolName: state.toolName,
        output: 'The user did not authorize the command in time (timed out). Acknowledge briefly.'
    });
}

export async function handleButtonInteraction(interaction) {
    const customId = interaction.customId;
    const parts = customId.split('_');
    const token = parts[3];
    const isOk = customId.endsWith('_ok');
    const guildId = interaction.guildId;

    const state = pendingConfirmations.get(token);
    if (!state) {
        return interaction.reply({
            content: MessagesService.get(guildId, 'Chat:ComandoExpirado'),
            ephemeral: true
        });
    }

    if (interaction.user.id !== state.userId) {
        return interaction.reply({
            content: MessagesService.get(guildId, 'Chat:ConfirmacionSoloSolicitante'),
            ephemeral: true
        });
    }

    clearTimeout(state.timeoutId);
    pendingConfirmations.delete(token);

    await interaction.deferUpdate().catch(() => {});
    await disableButtons(state);

    if (!isOk) {
        await resumeAiTool(state.ctx, {
            callId: state.callId,
            toolName: state.toolName,
            output: 'The user rejected executing the command. Acknowledge briefly.'
        });
        return;
    }

    const tool = getToolByName(state.toolName);
    let result;
    if (tool) {
        try {
            result = await tool.execute(state.ctx, state.args);
        } catch (e) {
            console.error('[aiConfirmation] Error executing confirmed tool:', e);
            result = {
                success: false,
                text: MessagesService.get(guildId, 'Chat:ErrorEjecucion'),
                description: state.commandDescription
            };
        }
    } else {
        result = {
            success: false,
            text: MessagesService.get(guildId, 'Chat:ErrorEjecucion'),
            description: state.commandDescription
        };
    }

    const outcome = await resumeAiTool(state.ctx, {
        callId: state.callId,
        toolName: state.toolName,
        output: result.text
    });

    const embed = new EmbedBuilder()
        .setTitle(result.description || state.commandDescription)
        .setDescription(result.text)
        .setColor(result.success ? 0x2ECC71 : 0xE74C3C);

    await state.ctx.channel.send({
        content: outcome.text || undefined,
        embeds: [embed]
    });
}

async function disableButtons(state) {
    const disabledRow = new ActionRowBuilder().addComponents(
        new ButtonBuilder().setCustomId('snowflake_done_ok').setLabel('✓').setStyle(ButtonStyle.Success).setDisabled(true),
        new ButtonBuilder().setCustomId('snowflake_done_no').setLabel('✕').setStyle(ButtonStyle.Danger).setDisabled(true)
    );

    try {
        if (state.message) {
            await state.message.edit({ components: [disabledRow] }).catch(() => {});
            setTimeout(() => {
                state.message?.delete().catch(() => {});
            }, 3000);
        }
    } catch {
        // Ignore edit failures
    }
}
