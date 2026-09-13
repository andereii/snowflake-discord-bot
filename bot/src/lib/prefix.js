const COMMAND_ALIASES = {
    'charlar': 'talk', 'conversar': 'talk',
    'charlar-limpiar': 'talk-clear', 'conversar-limpar': 'talk-clear',
    'ia-menciones': 'ai-mentions', 'ia-mencoes': 'ai-mentions',
    'reproducir': 'play', 'tocar': 'play',
    'saltar': 'skip', 'pular': 'skip',
    'detener': 'stop', 'parar': 'stop',
    'pausar': 'pause',
    'reanudar': 'resume', 'retomar': 'resume',
    'volumen': 'volume',
    'cola': 'queue', 'fila': 'queue', 'q': 'queue',
    'aleatorio': 'shuffle',
    'ir-a': 'seek', 'ir-para': 'seek',
    'repetir': 'replay',
    'bucle': 'loop',
    'bucle-ab': 'loop-ab',
    'graves': 'bassboost',
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
    'ia-espontaneo': 'ai-spontaneous',
    'descargar': 'download', 'baixar': 'download', 'dl': 'download',
    'imagen': 'image', 'imagem': 'image', 'img': 'image',
    'desaislar': 'untimeout', 'dessilenciar': 'untimeout',
    'historial': 'history', 'historico': 'history', 'logs': 'history',
    'silenciar': 'mute',
    'canal-logs': 'modlog', 'canal-de-logs': 'modlog',
    'bloquear': 'lock',
    'desbloquear': 'unlock',
    'canal': 'channel',
    'conteo': 'counting', 'contagem': 'counting',
    'descargar-cumple': 'birthday-remove',
    'cumpleaños': 'birthday', 'aniversario': 'birthday',
    'cumpleaños-quitar': 'birthday-remove', 'aniversario-remover': 'birthday-remove',
    'rol': 'role', 'cargo': 'role',
    'colores': 'colors', 'cores': 'colors'
};

const SUB_ALIASES = {
    'canal': 'channel',
    'desactivar': 'disable', 'desativar': 'disable',
    'oportunidades': 'chances',
    'objetivo': 'goal',
    'objetivo-quitar': 'goal-remove', 'objetivo-remover': 'goal-remove',
    'iconos': 'icons', 'icones': 'icons',
    'mensaje-perdida': 'lose-message', 'mensagem-perda': 'lose-message',
    'ranking': 'leaderboard',
    'estadisticas': 'stats', 'estatisticas': 'stats',
    'jugar': 'play', 'jogar': 'play',
    'crear': 'create', 'criar': 'create',
    'plantilla': 'template', 'modelo': 'template',
    'hub-quitar': 'hub-remove', 'hub-remover': 'hub-remove',
    'seguir': 'follow',
    'dejar': 'unfollow', 'deixar-de-seguir': 'unfollow',
    'rol': 'role', 'cargo': 'role',
    'mensaje': 'message', 'mensagem': 'message',
    'añadir': 'add', 'adicionar': 'add', 'agregar': 'add',
    'quitar': 'remove', 'remover': 'remove',
    'instalar': 'install', 'desinstalar': 'uninstall',
    'elegir': 'choose', 'escolher': 'choose',
    'listar': 'list',
    'ignored': 'ignored',
    'removeall': 'removeall'
};

export const PREFIX = ';';

export function resolveCommandName(input) {
    return COMMAND_ALIASES[input] || input;
}

function optionIndex(options, name) {
    return options.findIndex(opt => opt.name === name);
}

export function createPrefixInteraction(message, client, command, args) {
    const rawOptions = command.data.options || [];
    const hasSubs = rawOptions.some(o => o.type === 1 || o.options !== undefined);

    let activeSubcommand = null;
    let effectiveArgs = [...args];
    let effectiveOptions = rawOptions;

    if (hasSubs && args.length > 0) {
        const potential = SUB_ALIASES[args[0].toLowerCase()] || args[0].toLowerCase();
        const matched = rawOptions.find(o => o.name === potential);
        if (matched) {
            activeSubcommand = matched.name;
            effectiveArgs = args.slice(1);
            effectiveOptions = matched.options || [];
        }
    }
    if (hasSubs && !activeSubcommand && rawOptions[0]) {
        activeSubcommand = rawOptions[0].name;
        effectiveOptions = rawOptions[0].options || [];
    }

    let botReply = null;
    const argAt = (name) => {
        const index = optionIndex(effectiveOptions, name);
        if (index === -1 || index >= effectiveArgs.length) return null;
        if (index === effectiveOptions.length - 1) return effectiveArgs.slice(index).join(' ');
        return effectiveArgs[index];
    };

    const interaction = {
        isChatInputCommand: () => true,
        commandName: command.data.name,
        guildId: message.guildId,
        guild: message.guild,
        channelId: message.channelId,
        channel: message.channel,
        member: message.member,
        user: message.author,
        client,
        deferred: false,
        replied: false,
        options: {
            getSubcommand: () => activeSubcommand,
            getString: (name) => argAt(name),
            getInteger: (name) => {
                const raw = argAt(name);
                if (raw == null) return null;
                const parsed = parseInt(raw, 10);
                return Number.isNaN(parsed) ? null : parsed;
            },
            getBoolean: (name) => {
                const raw = argAt(name)?.toLowerCase();
                if (raw == null) return null;
                return raw === 'true' || raw === '1' || raw === 'si' || raw === 'on';
            },
            getUser: (name) => {
                const raw = argAt(name);
                if (!raw) return null;
                return client.users.cache.get(raw.replace(/[<@!>]/g, ''));
            },
            getMember: (name) => {
                const raw = argAt(name);
                if (!raw) return null;
                return message.guild.members.cache.get(raw.replace(/[<@!>]/g, ''));
            },
            getChannel: (name) => {
                const raw = argAt(name);
                if (raw) {
                    const channel = message.guild.channels.cache.get(raw.replace(/[<#>]/g, ''));
                    if (channel) return channel;
                }
                return message.channel;
            },
            getAttachment: () => message.attachments.first() || null,
            getRole: (name) => {
                const raw = argAt(name);
                if (!raw) return null;
                const id = raw.replace(/[<@&>]/g, '');
                return message.guild.roles.cache.get(id)
                    || message.guild.roles.cache.find(r => r.name.toLowerCase() === raw.toLowerCase())
                    || null;
            }
        },
        deferReply: async () => {
            interaction.deferred = true;
            botReply = await message.reply('> …');
            return botReply;
        },
        reply: async (opts) => {
            interaction.replied = true;
            const payload = typeof opts === 'string' ? { content: opts } : opts;
            botReply = await message.reply(payload);
            return botReply;
        },
        editReply: async (opts) => {
            const payload = typeof opts === 'string' ? { content: opts } : opts;
            if (botReply) return botReply.edit(payload);
            return message.reply(payload);
        },
        followUp: async (opts) => {
            const payload = typeof opts === 'string' ? { content: opts } : opts;
            return message.channel.send(payload);
        },
        deleteReply: async () => {
            if (botReply) await botReply.delete().catch(() => {});
        }
    };

    return interaction;
}
