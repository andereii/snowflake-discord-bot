import { askAiIsolated, isGeneratedMessage, getGeneratedMessageGuild } from './ai.js';
import { getGuildConfig, isEnabled } from './guildConfig.js';
import { PREFIX } from '../lib/prefix.js';

const BASE = Math.max(1, Number(process.env.AI_SPONTANEOUS_BASE || 100));
const JITTER_MIN = Math.max(0, Number(process.env.AI_SPONTANEOUS_JITTER_MIN || 1));
const JITTER_MAX = Math.max(JITTER_MIN, Number(process.env.AI_SPONTANEOUS_JITTER_MAX || 50));
const BUFFER = Math.max(1, Number(process.env.AI_SPONTANEOUS_BUFFER || 15));

const states = new Map();

function nextThreshold() {
    return BASE + JITTER_MIN + Math.floor(Math.random() * (JITTER_MAX - JITTER_MIN + 1));
}

function noteMessage(guildId, author, text) {
    let state = states.get(guildId);
    if (!state) {
        state = { count: 0, threshold: nextThreshold(), recent: [] };
        states.set(guildId, state);
    }
    state.recent.push({ author, text });
    while (state.recent.length > BUFFER) state.recent.shift();
    state.count += 1;
    if (state.count < state.threshold) return false;
    state.count = 0;
    state.threshold = nextThreshold();
    return true;
}

function buildPrompt(recent) {
    const lines = [
        "This is the server's recent conversation (the users are not talking to you directly, you are reading the chat):",
        ''
    ];
    for (const item of recent) {
        lines.push(`${item.author}: ${item.text}`);
    }
    lines.push(
        '',
        'Make a short, casual and natural comment, as if you were just another server member talking from the chat app.',
        'You can greet someone or pick up on something that was discussed.',
        'Do not mention that you are an AI or that anyone asked you to do this.',
        'Reply only with the message, without quotes or tags.'
    );
    return lines.join('\n');
}

async function speak(message, recent) {
    try {
        const ctx = {
            client: message.client,
            guild: message.guild,
            channel: message.channel,
            member: message.guild.members.me
        };
        const outcome = await askAiIsolated(ctx, buildPrompt(recent));
        const text = (outcome.text || '').trim();
        if (!text) return;
        await message.channel.send(text.slice(0, 2000));
    } catch (err) {
        console.warn('[aiSpontaneous]', err.message);
    }
}

export async function maybeSpontaneousComment(message, client) {
    if (!message.guildId || message.author.bot) return;
    const cfg = getGuildConfig(message.guildId);
    if (!isEnabled(cfg, 'AiSpontaneousEnabled', false)) return;

    const text = message.content?.trim();
    if (!text || text.startsWith(PREFIX) || text.startsWith('/')) return;
    if (message.mentions.has(client.user.id)) return;

    const referenced = message.reference?.messageId;
    if (referenced && isGeneratedMessage(referenced)
        && getGeneratedMessageGuild(referenced) === message.guildId) {
        return;
    }

    const fire = noteMessage(message.guildId, message.author.username, text);
    if (!fire) return;
    const recent = states.get(message.guildId)?.recent?.slice() || [];
    if (!recent.length) return;
    void speak(message, recent);
}
