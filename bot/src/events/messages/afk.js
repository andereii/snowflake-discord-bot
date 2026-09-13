import afkService from '../../services/afk.js';
import MessagesService from '../../services/messagesService.js';

export default async function afkHandler(message) {
    if (message.author.bot || !message.guild) return;

    const guildId = message.guild.id;
    const userId = message.author.id;
    const ignored = afkService.isIgnoredChannel(guildId, message.channel.id);

    if (!ignored) {
        const afkData = afkService.getAfk(guildId, userId);
        if (afkData && Date.now() - afkData.timestamp >= 3000) {
            const member = message.member || await message.guild.members.fetch(userId).catch(() => null);
            if (member) await afkService.removeAfk(member);
            else afkService.removeAfk({ guild: message.guild, id: userId, manageable: false });

            const text = MessagesService.get(guildId, 'Afk:BienvenidaRetorno', {
                usuario: message.author.toString(),
                tiempo: `<t:${Math.floor(afkData.timestamp / 1000)}:R>`
            });
            try {
                const sent = await message.channel.send(text);
                setTimeout(() => sent.delete().catch(() => {}), 10_000);
            } catch { /* ignore */ }
        }
    }

    if (!message.mentions.users.size) return;

    for (const [mentionedId, mentioned] of message.mentions.users) {
        if (mentionedId === userId || mentioned.bot) continue;
        const target = afkService.getAfk(guildId, mentionedId);
        if (!target) continue;
        if (afkService.isOnCooldown(guildId, message.channel.id, mentionedId)) continue;

        const text = MessagesService.get(guildId, 'Afk:MencionAusente', {
            usuario: mentioned.username,
            tiempo: `<t:${Math.floor(target.timestamp / 1000)}:R>`,
            motivo: target.reason
        });
        try {
            await message.channel.send(text);
        } catch { /* ignore */ }
    }
}
