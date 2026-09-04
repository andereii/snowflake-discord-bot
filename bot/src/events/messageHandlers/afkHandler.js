import afkService from '../../services/afk.js';
import MessagesService from '../../services/messagesService.js';

export default async function(message, client) {
    if (message.author.bot || !message.guild) return;

    const guildId = message.guild.id;
    const userId = message.author.id;

    // 1. Detección de retorno de usuario AFK
    const afkData = afkService.getAfk(guildId, userId);
    if (afkData) {
        const duracion = Date.now() - afkData.timestamp;
        
        if (duracion >= 3000) {
            afkService.removeAfk(guildId, userId);

            if (message.member && message.member.manageable) {
                try {
                    const currentNick = message.member.nickname || message.author.username;
                    if (currentNick.startsWith('[AFK] ')) {
                        await message.member.setNickname(afkData.originalNickname || null);
                    }
                } catch {
                    // Ignorar errores de permisos
                }
            }

            const timestampRelativo = `<t:${Math.floor(afkData.timestamp / 1000)}:R>`;
            const textoRetorno = MessagesService.get(guildId, 'Afk:BienvenidaRetorno', {
                usuario: message.author.toString(),
                tiempo: timestampRelativo
            });
            
            try {
                const msgRetorno = await message.channel.send(textoRetorno);
                setTimeout(() => {
                    msgRetorno.delete().catch(() => {});
                }, 10000);
            } catch {
                // Ignorar permisos
            }
        }
    }

    // 2. Detección de menciones a usuarios AFK
    if (message.mentions.users.size > 0) {
        for (const [mentionedId, mencionado] of message.mentions.users) {
            if (mentionedId === userId || mencionado.bot) continue;

            const afkTarget = afkService.getAfk(guildId, mentionedId);
            if (afkTarget) {
                if (afkService.isOnCooldown(guildId, message.channel.id, mentionedId)) continue;

                const timestampRelativo = `<t:${Math.floor(afkTarget.timestamp / 1000)}:R>`;
                const textoMencion = MessagesService.get(guildId, 'Afk:MencionAusente', {
                    usuario: mencionado.username,
                    tiempo: timestampRelativo,
                    motivo: `*${afkTarget.reason}*`
                });
                
                try {
                    await message.channel.send(textoMencion);
                } catch {
                    // Ignorar permisos
                }
            }
        }
    }
}
