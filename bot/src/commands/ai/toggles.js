import { PermissionFlagsBits } from 'discord.js';
import { slash, stateOption } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import { getGuildConfig, updateGuildConfig, isEnabled } from '../../services/guildConfig.js';
import MessagesService from '../../services/messagesService.js';

function hasAiKey() {
    return Boolean(process.env.DEEPSEEK_API_KEY || process.env.GEMINI_API_KEY);
}

function toggleCommand({ name, names, description, descriptions, column, onKey, offKey, defaultEnabled, missingKey }) {
    const data = stateOption(
        slash(name, description, {
            names: esPt(names.es, names.pt),
            descriptions: esPt(descriptions.es, descriptions.pt),
            permissions: PermissionFlagsBits.ManageGuild
        })
    );

    return {
        data,
        async execute(interaction) {
            const guildId = interaction.guildId;
            const state = interaction.options.getString('state');

            if (state === 'on' || state === 'off') {
                if (state === 'on' && missingKey && !hasAiKey()) {
                    return interaction.reply({
                        content: MessagesService.get(guildId, missingKey),
                        ephemeral: true
                    });
                }
                updateGuildConfig(guildId, { [column]: state === 'on' ? 1 : 0 });
                return interaction.reply(MessagesService.get(guildId, state === 'on' ? onKey : offKey));
            }

            const cfg = getGuildConfig(guildId);
            const enabled = isEnabled(cfg, column, defaultEnabled);
            return interaction.reply({
                content: MessagesService.get(guildId, enabled ? onKey : offKey),
                ephemeral: true
            });
        }
    };
}

export const commands = [
    toggleCommand({
        name: 'ai-mentions',
        names: { es: 'ia-menciones', pt: 'ia-mencoes' },
        description: 'Enable or disable AI replies when the bot is @mentioned',
        descriptions: { es: 'Activa o desactiva las respuestas por mención (@)', pt: 'Ativa ou desativa respostas por menção (@)' },
        column: 'AiMentionsEnabled',
        onKey: 'Chat:MencionesActivadas',
        offKey: 'Chat:MencionesDesactivadas',
        defaultEnabled: false
    }),
    toggleCommand({
        name: 'ai-search',
        names: { es: 'ia-busqueda', pt: 'ia-busca' },
        description: 'Enable or disable AI internet search',
        descriptions: { es: 'Activa o desactiva la búsqueda web de la IA', pt: 'Ativa ou desativa a busca web da IA' },
        column: 'AiWebSearchEnabled',
        onKey: 'Chat:BusquedaActivada',
        offKey: 'Chat:BusquedaDesactivada',
        defaultEnabled: true
    }),
    toggleCommand({
        name: 'ai-commands',
        names: { es: 'ia-comandos', pt: 'ia-comandos' },
        description: 'Enable or disable executing bot commands from AI chat',
        descriptions: { es: 'Activa o desactiva la ejecución de comandos por IA', pt: 'Ativa ou desativa a execução de comandos por IA' },
        column: 'AiCommandsEnabled',
        onKey: 'Chat:ComandosActivados',
        offKey: 'Chat:ComandosDesactivados',
        defaultEnabled: true
    }),
    toggleCommand({
        name: 'ai-spontaneous',
        names: { es: 'ia-espontaneo', pt: 'ia-espontaneo' },
        description: 'Enable or disable the bot talking on its own in chat (no mentions)',
        descriptions: {
            es: 'Activa o desactiva que el bot hable solo en el chat (sin menciones)',
            pt: 'Ativa ou desativa o bot falar sozinho no chat (sem menções)'
        },
        column: 'AiSpontaneousEnabled',
        onKey: 'Chat:EspontaneoActivado',
        offKey: 'Chat:EspontaneoDesactivado',
        defaultEnabled: false,
        missingKey: 'Chat:EspontaneoFaltaApiKey'
    })
];
