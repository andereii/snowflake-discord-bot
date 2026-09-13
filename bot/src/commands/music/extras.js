import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import {
    shuffleQueue,
    seekTo,
    replayCurrent,
    setLoopMode,
    setLoopAb,
    setPitch,
    setBass,
    parseTimestamp,
    parseLoopDuration,
    formatDuration,
    formatLoopReply
} from '../../services/music.js';
import MessagesService from '../../services/messagesService.js';
import { requireQueue, requireControl, refreshWidget } from './_helpers.js';

export const commands = [
    {
        data: slash('shuffle', 'Shuffle the upcoming songs in the queue', {
            names: esPt('aleatorio'),
            descriptions: esPt('Aleatoriza el orden de la cola', 'Aleatoriza a ordem da fila')
        }),
        async execute(interaction) {
            const ctx = await requireQueue(interaction);
            if (!ctx) return;
            if (!await requireControl(interaction)) return;
            const ok = shuffleQueue(ctx.guildId);
            await interaction.reply(ok
                ? MessagesService.get(ctx.guildId, 'Musica:Aleatorizado')
                : MessagesService.get(ctx.guildId, 'Musica:ColaVacia')
            );
            if (ok) await refreshWidget(interaction);
        }
    },
    {
        data: slash('seek', 'Jump to a timestamp in the current song', {
            names: esPt('ir-a', 'ir-para'),
            descriptions: esPt(
                'Salta a una posición de la canción (ej: 3:14)',
                'Pula para uma posição da música (ex: 3:14)'
            )
        }).addStringOption(option =>
            option.setName('position').setDescription('Timestamp (3:14, 1:02:30, or 90)').setRequired(true)
        ),
        async execute(interaction) {
            const ctx = await requireQueue(interaction);
            if (!ctx) return;
            if (!await requireControl(interaction)) return;
            const raw = interaction.options.getString('position');
            const seconds = parseTimestamp(raw);
            if (seconds == null) {
                return interaction.reply({
                    content: MessagesService.get(ctx.guildId, 'Musica:TimestampInvalido'),
                    ephemeral: true
                });
            }
            const result = seekTo(ctx.guildId, seconds);
            if (!result.ok) {
                return interaction.reply({
                    content: MessagesService.get(ctx.guildId, 'Musica:ErrorSaltar'),
                    ephemeral: true
                });
            }
            await interaction.reply(MessagesService.get(ctx.guildId, 'Musica:SaltadoA', {
                posicion: formatDuration(result.seconds, false, ctx.guildId)
            }));
            await refreshWidget(interaction);
        }
    },
    {
        data: slash('replay', 'Restart the current song from the beginning', {
            names: esPt('repetir'),
            descriptions: esPt('Reinicia la canción actual desde el inicio', 'Reinicia a música atual do começo')
        }),
        async execute(interaction) {
            const ctx = await requireQueue(interaction);
            if (!ctx) return;
            if (!await requireControl(interaction)) return;
            const result = replayCurrent(ctx.guildId);
            if (!result.ok) {
                return interaction.reply({
                    content: MessagesService.get(ctx.guildId, 'Musica:ErrorSaltar'),
                    ephemeral: true
                });
            }
            await interaction.reply(MessagesService.get(ctx.guildId, 'Musica:Replay'));
            await refreshWidget(interaction);
        }
    },
    {
        data: slash('loop', 'Loop the current track or the whole queue', {
            names: esPt('bucle'),
            descriptions: esPt(
                'Repite la pista o la cola; opcionalmente un número de reinicios o un tiempo',
                'Repete a faixa ou a fila; opcionalmente reinícios ou um tempo'
            )
        })
            .addStringOption(option =>
                option.setName('mode')
                    .setDescription('Loop mode')
                    .setRequired(true)
                    .addChoices(
                        { name: 'Off', value: 'off' },
                        { name: 'Track', value: 'track' },
                        { name: 'Queue', value: 'queue' }
                    )
            )
            .addIntegerOption(option =>
                option.setName('repeats')
                    .setDescription('Disable after this many restarts (e.g. 3)')
                    .setMinValue(1)
                    .setMaxValue(99)
            )
            .addStringOption(option =>
                option.setName('duration')
                    .setDescription('Disable after this long (5m, 1h, 90, 3:00)')
            ),
        async execute(interaction) {
            const ctx = await requireQueue(interaction, { playing: false });
            if (!ctx) return;
            if (!await requireControl(interaction)) return;

            const mode = interaction.options.getString('mode');
            const repeats = interaction.options.getInteger('repeats');
            const rawDuration = interaction.options.getString('duration');
            let durationMs;
            if (rawDuration) {
                durationMs = parseLoopDuration(rawDuration);
                if (durationMs == null || durationMs < 1000 || durationMs > 24 * 3_600_000) {
                    return interaction.reply({
                        content: MessagesService.get(ctx.guildId, 'Musica:LoopDuracionInvalida'),
                        ephemeral: true
                    });
                }
            }

            const result = setLoopMode(ctx.guildId, mode, { repeats, durationMs });
            await interaction.reply(formatLoopReply(ctx.guildId, result));
            await refreshWidget(interaction);
        }
    },
    {
        data: slash('loop-ab', 'Loop a section of the current song (A-B)', {
            names: esPt('bucle-ab'),
            descriptions: esPt(
                'Repite un tramo de la canción (puntos A y B)',
                'Repete um trecho da música (pontos A e B)'
            )
        })
            .addStringOption(option =>
                option.setName('action')
                    .setDescription('Set point A, set point B, or turn off')
                    .setRequired(true)
                    .addChoices(
                        { name: 'Set A', value: 'set-a' },
                        { name: 'Set B', value: 'set-b' },
                        { name: 'Off', value: 'off' }
                    )
            )
            .addStringOption(option =>
                option.setName('position').setDescription('Optional timestamp (default: current position)').setRequired(false)
            ),
        async execute(interaction) {
            const ctx = await requireQueue(interaction);
            if (!ctx) return;
            if (!await requireControl(interaction)) return;
            const action = interaction.options.getString('action');
            const rawPos = interaction.options.getString('position');
            let position;
            if (rawPos) {
                position = parseTimestamp(rawPos);
                if (position == null) {
                    return interaction.reply({
                        content: MessagesService.get(ctx.guildId, 'Musica:TimestampInvalido'),
                        ephemeral: true
                    });
                }
            }
            const result = setLoopAb(ctx.guildId, action, position);
            if (!result.ok) {
                const key = result.reason === 'need-a' ? 'Musica:LoopABFaltaA' : 'Musica:ErrorSaltar';
                return interaction.reply({ content: MessagesService.get(ctx.guildId, key), ephemeral: true });
            }
            let key = 'Musica:LoopABOff';
            const vars = {};
            if (result.action === 'set-a') {
                key = 'Musica:LoopABPuntoA';
                vars.posicion = formatDuration(result.position, false, ctx.guildId);
            } else if (result.action === 'set-b') {
                key = 'Musica:LoopABPuntoB';
                vars.posicion = formatDuration(result.position, false, ctx.guildId);
            }
            await interaction.reply(MessagesService.get(ctx.guildId, key, vars));
            await refreshWidget(interaction);
        }
    },
    {
        data: slash('nightcore', 'Toggle the nightcore audio profile', {
            descriptions: esPt('Activa o desactiva el perfil nightcore', 'Ativa ou desativa o perfil nightcore')
        }).addStringOption(option =>
            option.setName('state')
                .setDescription('On, off, or omit to toggle')
                .addChoices(
                    { name: 'On', value: 'on' },
                    { name: 'Off', value: 'off' }
                )
        ),
        async execute(interaction) {
            const ctx = await requireQueue(interaction, { playing: false });
            if (!ctx) return;
            if (!await requireControl(interaction)) return;
            const state = interaction.options.getString('state');
            const pitch = setPitch(ctx.guildId, state === 'on' ? 'nightcore' : state === 'off' ? 'off' : 'toggle-nightcore');
            await interaction.reply(MessagesService.get(
                ctx.guildId,
                pitch === 'nightcore' ? 'Musica:NightcoreOn' : 'Musica:NightcoreOff'
            ));
            await refreshWidget(interaction);
        }
    },
    {
        data: slash('daycore', 'Toggle the daycore audio profile', {
            descriptions: esPt('Activa o desactiva el perfil daycore', 'Ativa ou desativa o perfil daycore')
        }).addStringOption(option =>
            option.setName('state')
                .setDescription('On, off, or omit to toggle')
                .addChoices(
                    { name: 'On', value: 'on' },
                    { name: 'Off', value: 'off' }
                )
        ),
        async execute(interaction) {
            const ctx = await requireQueue(interaction, { playing: false });
            if (!ctx) return;
            if (!await requireControl(interaction)) return;
            const state = interaction.options.getString('state');
            const pitch = setPitch(ctx.guildId, state === 'on' ? 'daycore' : state === 'off' ? 'off' : 'toggle-daycore');
            await interaction.reply(MessagesService.get(
                ctx.guildId,
                pitch === 'daycore' ? 'Musica:DaycoreOn' : 'Musica:DaycoreOff'
            ));
            await refreshWidget(interaction);
        }
    },
    {
        data: slash('bassboost', 'Set bass boost intensity (0 disables it)', {
            names: esPt('graves'),
            descriptions: esPt(
                'Ajusta el realce de graves (0 lo apaga)',
                'Ajusta o realce de graves (0 desliga)'
            )
        }).addIntegerOption(option =>
            option.setName('intensity').setDescription('0-100 (0 = off)').setRequired(true).setMinValue(0).setMaxValue(100)
        ),
        async execute(interaction) {
            const ctx = await requireQueue(interaction, { playing: false });
            if (!ctx) return;
            if (!await requireControl(interaction)) return;
            const level = setBass(ctx.guildId, interaction.options.getInteger('intensity'));
            await interaction.reply(MessagesService.get(ctx.guildId, 'Musica:BassBoost', { nivel: level }));
            await refreshWidget(interaction);
        }
    }
];
