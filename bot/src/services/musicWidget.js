import {
    ActionRowBuilder,
    ButtonBuilder,
    ButtonStyle,
    EmbedBuilder
} from 'discord.js';
import MessagesService from './messagesService.js';
import {
    getQueue,
    getPlaybackState,
    buildQueueEmbed,
    dspLabel,
    formatDuration,
    canControlMusic,
    pausePlayback,
    resumePlayback,
    isPaused,
    skipCurrent,
    stopPlayback,
    shuffleQueue,
    cycleLoopMode,
    replayCurrent,
    setMusicUi
} from './music.js';

export const BTN_PAUSE = 'snowflake_music_pause';
export const BTN_SKIP = 'snowflake_music_skip';
export const BTN_QUEUE = 'snowflake_music_cola';
export const BTN_STOP = 'snowflake_music_stop';
export const BTN_SHUFFLE = 'snowflake_music_shuffle';
export const BTN_LOOP = 'snowflake_music_loop';
export const BTN_REPLAY = 'snowflake_music_replay';

const MUSIC_IDS = [BTN_PAUSE, BTN_SKIP, BTN_QUEUE, BTN_STOP, BTN_SHUFFLE, BTN_LOOP, BTN_REPLAY];
const CONTROL_IDS = new Set([BTN_PAUSE, BTN_SKIP, BTN_STOP, BTN_SHUFFLE, BTN_LOOP, BTN_REPLAY]);

const widgets = new Map();
const widgetLocks = new Map();
const DELETE_DELAY_MS = 5000;

function withWidgetLock(guildId, fn) {
    const previous = widgetLocks.get(guildId) || Promise.resolve();
    const next = previous.then(fn, fn);
    widgetLocks.set(guildId, next.catch(() => {}));
    return next;
}

async function deleteExisting(guildId, channel) {
    const existing = widgets.get(guildId);
    if (!existing) return;
    widgets.delete(guildId);
    const target = channel || existing.channel;
    if (!target) return;
    try {
        const message = await target.messages.fetch(existing.messageId);
        await message.delete();
    } catch { /* already gone */ }
}

export function isMusicWidgetInteraction(customId) {
    return MUSIC_IDS.includes(customId);
}

function buildButtons(disabled = false) {
    return [
        new ActionRowBuilder().addComponents(
            new ButtonBuilder().setCustomId(BTN_PAUSE).setEmoji('⏯️').setStyle(ButtonStyle.Primary).setDisabled(disabled),
            new ButtonBuilder().setCustomId(BTN_SKIP).setEmoji('⏭️').setStyle(ButtonStyle.Secondary).setDisabled(disabled),
            new ButtonBuilder().setCustomId(BTN_QUEUE).setEmoji('📋').setStyle(ButtonStyle.Secondary).setDisabled(disabled),
            new ButtonBuilder().setCustomId(BTN_STOP).setEmoji('⏹️').setStyle(ButtonStyle.Danger).setDisabled(disabled)
        ),
        new ActionRowBuilder().addComponents(
            new ButtonBuilder().setCustomId(BTN_SHUFFLE).setEmoji('🔀').setStyle(ButtonStyle.Secondary).setDisabled(disabled),
            new ButtonBuilder().setCustomId(BTN_LOOP).setEmoji('🔁').setStyle(ButtonStyle.Secondary).setDisabled(disabled),
            new ButtonBuilder().setCustomId(BTN_REPLAY).setEmoji('⏮️').setStyle(ButtonStyle.Secondary).setDisabled(disabled)
        )
    ];
}

export function buildNowPlayingEmbed(guildId) {
    const state = getPlaybackState(guildId);
    if (!state?.song) {
        return new EmbedBuilder()
            .setDescription(MessagesService.get(guildId, 'Musica:WidgetSinPista'))
            .setColor(0x99AAB5);
    }

    const song = state.song;
    const status = state.paused
        ? MessagesService.get(guildId, 'Musica:EstadoPausado')
        : MessagesService.get(guildId, 'Musica:EstadoReproduciendo');

    const embed = new EmbedBuilder()
        .setTitle(MessagesService.get(guildId, 'Musica:WidgetTitulo'))
        .setDescription(`**[${song.title}](${song.url})**\n${song.author}`)
        .setColor(0x5865F2)
        .addFields(
            { name: MessagesService.get(guildId, 'Musica:WidgetEstado'), value: status, inline: true },
            {
                name: MessagesService.get(guildId, 'Musica:WidgetDuracion'),
                value: formatDuration(song.duration, song.isLive, guildId),
                inline: true
            }
        );

    if (song.thumbnail) embed.setThumbnail(song.thumbnail);
    const extras = dspLabel(guildId);
    if (extras) embed.setFooter({ text: extras });
    return embed;
}

function payload(guildId, { disabled = false } = {}) {
    return {
        embeds: [buildNowPlayingEmbed(guildId)],
        components: buildButtons(disabled)
    };
}

export async function sendOrUpdateWidget(channel, guildId) {
    if (!channel) return null;
    return withWidgetLock(guildId, async () => {
        await deleteExisting(guildId, channel);
        const message = await channel.send(payload(guildId));
        widgets.set(guildId, { messageId: message.id, channelId: channel.id, channel });
        return message;
    });
}

export async function refreshWidgetIfExists(guildId, channel) {
    const existing = widgets.get(guildId);
    if (!existing) return;
    const target = channel || existing.channel;
    if (!target) return;
    await sendOrUpdateWidget(target, guildId);
}

async function deleteWidgetLater(guildId, record) {
    try {
        await new Promise(resolve => setTimeout(resolve, DELETE_DELAY_MS));
        const channel = await fetchWidgetChannel(guildId, record);
        if (!channel) return;
        const message = await channel.messages.fetch(record.messageId).catch(() => null);
        if (message) await message.delete().catch(() => {});
    } catch { /* message already gone */ }
}

async function fetchWidgetChannel(guildId, record) {
    if (record?.channel) return record.channel;
    const queue = getQueue(guildId);
    if (queue?.textChannel) return queue.textChannel;
    return null;
}

export async function stopWidget(guildId, channel) {
    const existing = widgets.get(guildId);
    if (!existing) return;
    widgets.delete(guildId);
    const target = channel || existing.channel || await fetchWidgetChannel(guildId, existing);
    if (!target) return;

    try {
        const message = await target.messages.fetch(existing.messageId);
        await message.edit({
            embeds: [
                new EmbedBuilder()
                    .setDescription(MessagesService.get(guildId, 'Musica:ReproduccionDetenida'))
                    .setColor(0x99AAB5)
            ],
            components: buildButtons(true)
        });
        void deleteWidgetLater(guildId, existing);
    } catch { /* ignore */ }
}

export async function applyMusicWidgetFromCommands(channel, guildId, commands = []) {
    const flags = (commands || []).map(cmd => cmd.musicWidget).filter(Boolean);
    if (!flags.length) return;
    if (flags.includes('stop')) {
        await stopWidget(guildId, channel);
        return;
    }
    if (flags.includes('send')) {
        await sendOrUpdateWidget(channel, guildId);
        return;
    }
    await refreshWidgetIfExists(guildId, channel);
}

export async function handleMusicButton(interaction) {
    const guildId = interaction.guildId;
    const id = interaction.customId;

    if (id === BTN_QUEUE) {
        const embed = buildQueueEmbed(guildId);
        if (!embed) {
            await interaction.reply({
                content: MessagesService.get(guildId, 'Musica:ColaVacia'),
                ephemeral: true
            });
            return;
        }
        await interaction.reply({ embeds: [embed], ephemeral: true });
        return;
    }

    if (CONTROL_IDS.has(id)) {
        const access = canControlMusic(interaction);
        if (!access.ok) {
            await interaction.reply({ content: access.message, ephemeral: true });
            return;
        }
    }

    if (!getQueue(guildId) && id !== BTN_STOP) {
        await interaction.deferUpdate().catch(() => {});
        return;
    }

    await interaction.deferUpdate().catch(() => {});

    if (id === BTN_PAUSE) {
        if (isPaused(guildId)) resumePlayback(guildId);
        else pausePlayback(guildId);
        await sendOrUpdateWidget(interaction.channel, guildId);
        return;
    }

    if (id === BTN_SKIP) {
        skipCurrent(guildId);
        if (getQueue(guildId)) await sendOrUpdateWidget(interaction.channel, guildId);
        return;
    }

    if (id === BTN_STOP) {
        stopPlayback(guildId);
        return;
    }

    if (id === BTN_SHUFFLE) {
        shuffleQueue(guildId);
        await sendOrUpdateWidget(interaction.channel, guildId);
        return;
    }

    if (id === BTN_LOOP) {
        cycleLoopMode(guildId);
        await sendOrUpdateWidget(interaction.channel, guildId);
        return;
    }

    if (id === BTN_REPLAY) {
        replayCurrent(guildId);
        if (getQueue(guildId)) await sendOrUpdateWidget(interaction.channel, guildId);
    }
}

setMusicUi({
    onTrackChange: async (guildId, channel) => {
        if (channel) await sendOrUpdateWidget(channel, guildId).catch(() => {});
    },
    onStopped: async (guildId, channel) => {
        await stopWidget(guildId, channel).catch(() => {});
    }
});
