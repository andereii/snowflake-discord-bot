import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import {
    skipCurrent,
    stopPlayback,
    applyVolumeInput,
    pausePlayback,
    resumePlayback,
    songVars
} from '../../services/music.js';
import MessagesService from '../../services/messagesService.js';
import { requireQueue, requireControl, refreshWidget, endWidget } from './_helpers.js';

export const commands = [
    {
        data: slash('pause', 'Pause the current song', {
            names: esPt('pausar'),
            descriptions: esPt('Pausa la reproducción actual', 'Pausa a reprodução atual')
        }),
        async execute(interaction) {
            const ctx = await requireQueue(interaction);
            if (!ctx) return;
            if (!await requireControl(interaction)) return;
            pausePlayback(ctx.guildId);
            await interaction.reply(MessagesService.get(ctx.guildId, 'Musica:Pausado'));
            await refreshWidget(interaction);
        }
    },
    {
        data: slash('resume', 'Resume the paused song', {
            names: esPt('reanudar', 'retomar'),
            descriptions: esPt('Reanuda la reproducción pausada', 'Retoma a reprodução pausada')
        }),
        async execute(interaction) {
            const ctx = await requireQueue(interaction);
            if (!ctx) return;
            if (!await requireControl(interaction)) return;
            resumePlayback(ctx.guildId);
            await interaction.reply(MessagesService.get(ctx.guildId, 'Musica:Reanudado'));
            await refreshWidget(interaction);
        }
    },
    {
        data: slash('skip', 'Skip the currently playing song', {
            names: esPt('saltar', 'pular'),
            descriptions: esPt('Salta la canción actual', 'Pula a música atual')
        }),
        async execute(interaction) {
            const ctx = await requireQueue(interaction);
            if (!ctx) return;
            if (!await requireControl(interaction)) return;
            const next = skipCurrent(ctx.guildId);
            await interaction.reply(next
                ? MessagesService.get(ctx.guildId, 'Musica:SaltadoProxima', songVars(next, ctx.guildId))
                : MessagesService.get(ctx.guildId, 'Musica:SaltadoVacio')
            );
            await refreshWidget(interaction);
        }
    },
    {
        data: slash('stop', 'Stop music playback and clear the queue', {
            names: esPt('detener', 'parar'),
            descriptions: esPt('Detiene la música y vacía la cola', 'Para a música e limpa a fila')
        }),
        async execute(interaction) {
            const ctx = await requireQueue(interaction, { playing: false });
            if (!ctx) return;
            if (!await requireControl(interaction)) return;
            stopPlayback(ctx.guildId);
            await interaction.reply(MessagesService.get(ctx.guildId, 'Musica:Detenido'));
            await endWidget(interaction);
        }
    },
    {
        data: slash('volume', 'Set the music volume (0-100, +10, 30+20)', {
            names: esPt('volumen', 'volume'),
            descriptions: esPt(
                'Ajusta el volumen (0-100, relativo +10/-10, o 30+20)',
                'Ajusta o volume (0-100, relativo +10/-10, ou 30+20)'
            )
        }).addStringOption(option =>
            option.setName('level').setDescription('Volume: 50, +10, -5, or 30+20').setRequired(true)
        ),
        async execute(interaction) {
            const guildId = interaction.guildId;
            const result = applyVolumeInput(guildId, interaction.options.getString('level'));
            if (!result.ok) {
                return interaction.reply({
                    content: MessagesService.get(guildId, 'Musica:VolumenInvalido'),
                    ephemeral: true
                });
            }
            await interaction.reply(MessagesService.get(guildId, 'Musica:Volumen', { nivel: result.level }));
            await refreshWidget(interaction);
        }
    }
];
