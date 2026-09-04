export default async function prefixHandler(message, client) {
    const PREFIX = ';';
    if (!message.content.startsWith(PREFIX)) return;
    
    const args = message.content.slice(PREFIX.length).trim().split(/ +/);
    const inputCommandName = args.shift().toLowerCase();
    
    // Resolve Spanish/Portuguese alias to canonical English name
    const aliases = {
        'charlar': 'talk', 'conversar': 'talk',
        'charlar-limpiar': 'talk-clear', 'conversar-limpar': 'talk-clear',
        'ia-menciones': 'ai-mentions', 'ia-mencoes': 'ai-mentions',
        'reproducir': 'play', 'tocar': 'play',
        'saltar': 'skip', 'pular': 'skip',
        'detener': 'stop', 'parar': 'stop',
        'pausar': 'pause',
        'reanudar': 'resume', 'retomar': 'resume',
        'volumen': 'volume',
        'vetar': 'ban', 'banir': 'ban',
        'expulsar': 'kick',
        'aislar': 'timeout', 'isolar': 'timeout',
        'advertir': 'warn', 'avisar': 'warn',
        'dado': 'roll',
        'limpiar': 'clear', 'limpar': 'clear',
        'gato': 'cat',
        'idioma': 'lang', 'language': 'lang',
        'ver': 'show', 'config': 'show', 'configuracion': 'show',
        'ia-busqueda': 'ai-search', 'ia-busca': 'ai-search',
        'ia-comandos': 'ai-commands',
        'descargar': 'download', 'baixar': 'download', 'dl': 'download',
        'imagen': 'image', 'imagem': 'image', 'img': 'image',
        'desaislar': 'untimeout', 'dessilenciar': 'untimeout',
        'historial': 'history', 'historico': 'history', 'logs': 'history',
        'softban': 'softban',
        'mute': 'mute', 'silenciar': 'mute',
        'hardmute': 'hardmute',
        'unhardmute': 'unhardmute',
        'canal-logs': 'modlog', 'canal-de-logs': 'modlog', 'modlog': 'modlog',
        'bloquear': 'lock',
        'desbloquear': 'unlock',
        'canal': 'channel',
        'conteo': 'counting', 'contagem': 'counting', 'counting': 'counting',
        'trivia': 'trivia',
    };
    
    const commandName = aliases[inputCommandName] || inputCommandName;
    
    const command = client.commands.get(commandName);
    if (!command) return;

    const rawOptions = command.data.options || [];

    // Subcommand aliases mapping
    const subAliases = {
        'canal': 'channel',
        'desactivar': 'disable', 'desativar': 'disable',
        'base': 'base',
        'oportunidades': 'chances', 'chances': 'chances',
        'objetivo': 'goal',
        'objetivo-quitar': 'goal-remove', 'objetivo-remover': 'goal-remove',
        'iconos': 'icons', 'icones': 'icons',
        'mensaje-perdida': 'lose-message', 'mensagem-perda': 'lose-message',
        'leaderboard': 'leaderboard', 'ranking': 'leaderboard',
        'estadisticas': 'stats', 'estatisticas': 'stats', 'stats': 'stats',
        'jugar': 'play', 'jogar': 'play', 'play': 'play',
        'crear': 'create', 'criar': 'create',
        'plantilla': 'template', 'modelo': 'template',
        'hub-quitar': 'hub-remove', 'hub-remover': 'hub-remove',
        'seguir': 'follow',
        'dejar': 'unfollow', 'deixar-de-seguir': 'unfollow',
        'rol': 'role', 'cargo': 'role',
        'mensaje': 'message', 'mensagem': 'message'
    };

    let activeSubcommand = null;
    let effectiveArgs = [...args];
    let effectiveOptions = rawOptions;

    // Check if command has subcommands
    const hasSubcommands = rawOptions.some(opt => opt.type === 1 || opt.name);
    if (rawOptions.length > 0 && (rawOptions[0].options !== undefined || rawOptions.some(o => o.type === 1))) {
        if (args.length > 0) {
            const potentialSub = subAliases[args[0].toLowerCase()] || args[0].toLowerCase();
            const matchedSub = rawOptions.find(o => o.name === potentialSub);
            if (matchedSub) {
                activeSubcommand = matchedSub.name;
                effectiveArgs = args.slice(1);
                effectiveOptions = matchedSub.options || [];
            }
        }
        if (!activeSubcommand && rawOptions.length > 0) {
            activeSubcommand = rawOptions[0].name;
            effectiveOptions = rawOptions[0].options || [];
        }
    }
    
    // Store the bot's initial reply so editReply can edit IT instead of sending a new message
    let botReplyMessage = null;

    const mockInteraction = {
        isChatInputCommand: () => true,
        commandName,
        guildId: message.guildId,
        guild: message.guild,
        channelId: message.channelId,
        channel: message.channel,
        member: message.member,
        user: message.author,
        client: client,
        deferred: false,
        replied: false,
        options: {
            getSubcommand: () => activeSubcommand,
            getString: (name) => {
                const index = effectiveOptions.findIndex(opt => opt.name === name);
                if (index !== -1 && index < effectiveArgs.length) {
                    if (index === effectiveOptions.length - 1) {
                        return effectiveArgs.slice(index).join(' ');
                    }
                    return effectiveArgs[index];
                }
                return null;
            },
            getInteger: (name) => {
                const index = effectiveOptions.findIndex(opt => opt.name === name);
                if (index !== -1 && index < effectiveArgs.length) {
                    const parsed = parseInt(effectiveArgs[index], 10);
                    return isNaN(parsed) ? null : parsed;
                }
                return null;
            },
            getBoolean: (name) => {
                const index = effectiveOptions.findIndex(opt => opt.name === name);
                if (index !== -1 && index < effectiveArgs.length) {
                    const v = effectiveArgs[index].toLowerCase();
                    return v === 'true' || v === '1' || v === 'si' || v === 'on';
                }
                return null;
            },
            getUser: (name) => {
                const index = effectiveOptions.findIndex(opt => opt.name === name);
                if (index !== -1 && index < effectiveArgs.length) {
                    const mention = effectiveArgs[index].replace(/[<@!>]/g, '');
                    return client.users.cache.get(mention);
                }
                return null;
            },
            getMember: (name) => {
                const index = effectiveOptions.findIndex(opt => opt.name === name);
                if (index !== -1 && index < effectiveArgs.length) {
                    const mention = effectiveArgs[index].replace(/[<@!>]/g, '');
                    return message.guild.members.cache.get(mention);
                }
                return null;
            },
            getChannel: (name) => {
                const index = effectiveOptions.findIndex(opt => opt.name === name);
                if (index !== -1 && index < effectiveArgs.length) {
                    const id = effectiveArgs[index].replace(/[<#>]/g, '');
                    const ch = message.guild.channels.cache.get(id);
                    if (ch) return ch;
                }
                return message.channel;
            },
            getAttachment: () => message.attachments.first() || null,
        },
        deferReply: async () => {
            mockInteraction.deferred = true;
            botReplyMessage = await message.reply('> Pensando...');
        },
        reply: async (opts) => {
            mockInteraction.replied = true;
            const payload = typeof opts === 'string' ? { content: opts } : opts;
            // If ephemeral, just send normally (prefix commands can't be ephemeral)
            botReplyMessage = await message.reply(payload);
            return botReplyMessage;
        },
        editReply: async (opts) => {
            const payload = typeof opts === 'string' ? { content: opts } : opts;
            // Edit the existing bot reply instead of sending a new message
            if (botReplyMessage) {
                return await botReplyMessage.edit(payload);
            }
            // Fallback: if reply() was never called, just send a new message
            return await message.reply(payload);
        },
        followUp: async (opts) => {
            const payload = typeof opts === 'string' ? { content: opts } : opts;
            return await message.channel.send(payload);
        },
        deleteReply: async () => {
            if (botReplyMessage) {
                await botReplyMessage.delete().catch(() => {});
            }
        }
    };

    try {
        await command.execute(mockInteraction);
    } catch (error) {
        console.error(`[bot] Error ejecutando comando de prefijo ;${commandName}:`, error);
        await message.reply('Hubo un error al ejecutar este comando.').catch(() => {});
    }
}
