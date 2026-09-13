import { AttachmentBuilder } from 'discord.js';
import { spawn } from 'child_process';
import fs from 'fs';
import path from 'path';
import MessagesService from './messagesService.js';

export const activePolls = new Map();
export const NUMBER_EMOJIS = ['1️⃣', '2️⃣', '3️⃣', '4️⃣', '5️⃣', '6️⃣', '7️⃣', '8️⃣', '9️⃣', '🔟'];

export function registerPoll(messageId, poll) {
    activePolls.set(messageId, poll);
}

export function getPoll(messageId) {
    return activePolls.get(messageId);
}

export function handlePollReactionAdd(poll, userId, emojiName, removeUser) {
    if (!poll || poll.multiVote) return;
    if (poll.voters.has(userId)) {
        removeUser().catch(() => {});
        return;
    }
    poll.voters.set(userId, emojiName);
}

export function handlePollReactionRemove(poll, userId, emojiName) {
    if (!poll || poll.multiVote) return;
    if (poll.voters.get(userId) === emojiName) poll.voters.delete(userId);
}

export async function endPoll(message, client) {
    const poll = activePolls.get(message.id);
    if (!poll) return;
    activePolls.delete(message.id);

    const fetched = await message.channel.messages.fetch(message.id).catch(() => null);
    if (!fetched) return;

    for (const option of poll.options) {
        const reaction = fetched.reactions.cache.get(option.emoji);
        if (!reaction) continue;
        await reaction.users.fetch();
        option.votes = reaction.users.cache.filter(u => u.id !== client.user.id).size;
    }

    const payload = JSON.stringify({
        title: poll.question,
        options: poll.options.map(o => ({ label: o.text, count: o.votes }))
    });

    const binPath = path.resolve(process.cwd(), '../src/Dlang/piechart');
    const outPath = path.resolve(process.cwd(), `../data/poll_${poll.id}.png`);

    try {
        const proc = spawn(binPath, [outPath]);
        proc.stdin.write(payload);
        proc.stdin.end();

        await new Promise((resolve, reject) => {
            proc.on('close', code => (code === 0 ? resolve() : reject(new Error(`piechart exited ${code}`))));
            proc.on('error', reject);
        });

        const attachment = new AttachmentBuilder(outPath, { name: 'piechart.png' });
        await fetched.edit({
            embeds: [{
                title: poll.question,
                description: MessagesService.get(poll.guildId, 'Encuestas:FinalizadaDesc'),
                color: 0x2ecc71,
                image: { url: 'attachment://piechart.png' }
            }],
            files: [attachment]
        });
        fs.unlink(outPath, () => {});
    } catch (err) {
        console.error('[poll] piechart failed:', err);
        await fetched.edit({ content: MessagesService.get(poll.guildId, 'Encuestas:FinalizadaDesc') });
    }
}
