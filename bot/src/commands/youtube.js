import { SlashCommandBuilder, ChannelType, PermissionFlagsBits, EmbedBuilder } from 'discord.js';
import db from '../services/database.js';
import MessagesService from '../services/messagesService.js';
import youtubeService from '../services/youtubeService.js';

export const data = new SlashCommandBuilder()
    .setName('youtube')
    .setDescription('YouTube notification subscriptions')
    .setNameLocalizations({
        'es-ES': 'youtube',
        'es-419': 'youtube',
        'pt-BR': 'youtube'
    })
    .setDescriptionLocalizations({
        'es-ES': 'Suscripciones de notificaciones de YouTube',
        'es-419': 'Suscripciones de notificaciones de YouTube',
        'pt-BR': 'Inscrições de notificações do YouTube'
    })
    // Subcommand: follow
    .addSubcommand(sub =>
        sub.setName('follow')
            .setDescription('Subscribe the bot to a YouTube channel and announce new videos')
            .setNameLocalizations({
                'es-ES': 'seguir',
                'es-419': 'seguir',
                'pt-BR': 'seguir'
            })
            .setDescriptionLocalizations({
                'es-ES': 'Suscribe al bot a un canal de YouTube y avisa de vídeos nuevos',
                'es-419': 'Suscribe al bot a un canal de YouTube y avisa de vídeos nuevos',
                'pt-BR': 'Inscreve o bot em um canal do YouTube e avisa sobre vídeos novos'
            })
            .addStringOption(opt =>
                opt.setName('channel')
                    .setDescription('Channel URL or @handle (e.g. https://www.youtube.com/@mkbhd)')
                    .setNameLocalizations({
                        'es-ES': 'canal',
                        'es-419': 'canal',
                        'pt-BR': 'canal'
                    })
                    .setDescriptionLocalizations({
                        'es-ES': 'URL del canal o @handle (ej: https://www.youtube.com/@mkbhd)',
                        'es-419': 'URL del canal o @handle (ej: https://www.youtube.com/@mkbhd)',
                        'pt-BR': 'URL do canal ou @handle (ex.: https://www.youtube.com/@mkbhd)'
                    })
                    .setRequired(true)
            )
            .addChannelOption(opt =>
                opt.setName('notify')
                    .setDescription('Discord channel where the announcement is sent')
                    .setNameLocalizations({
                        'es-ES': 'notificar',
                        'es-419': 'notificar',
                        'pt-BR': 'notificar'
                    })
                    .setDescriptionLocalizations({
                        'es-ES': 'Canal de Discord donde enviar el aviso',
                        'es-419': 'Canal de Discord donde enviar el aviso',
                        'pt-BR': 'Canal do Discord onde o aviso é enviado'
                    })
                    .addChannelTypes(ChannelType.GuildText)
                    .setRequired(true)
            )
            .addRoleOption(opt =>
                opt.setName('role')
                    .setDescription('Role to mention in the announcement (optional)')
                    .setNameLocalizations({
                        'es-ES': 'rol',
                        'es-419': 'rol',
                        'pt-BR': 'cargo'
                    })
                    .setDescriptionLocalizations({
                        'es-ES': 'Rol a mencionar en el aviso (opcional)',
                        'es-419': 'Rol a mencionar en el aviso (opcional)',
                        'pt-BR': 'Cargo a mencionar no aviso (opcional)'
                    })
            )
    )
    // Subcommand: unfollow
    .addSubcommand(sub =>
        sub.setName('unfollow')
            .setDescription('Remove the server\'s YouTube subscription')
            .setNameLocalizations({
                'es-ES': 'dejar',
                'es-419': 'dejar',
                'pt-BR': 'deixar-de-seguir'
            })
            .setDescriptionLocalizations({
                'es-ES': 'Elimina la suscripción de YouTube del servidor',
                'es-419': 'Elimina la suscripción de YouTube del servidor',
                'pt-BR': 'Remove a inscrição do YouTube do servidor'
            })
    )
    // Subcommand: show
    .addSubcommand(sub =>
        sub.setName('show')
            .setDescription('Show the server\'s YouTube subscription')
            .setNameLocalizations({
                'es-ES': 'ver',
                'es-419': 'ver',
                'pt-BR': 'ver'
            })
            .setDescriptionLocalizations({
                'es-ES': 'Muestra la suscripción de YouTube del servidor',
                'es-419': 'Muestra la suscripción de YouTube del servidor',
                'pt-BR': 'Mostra a inscrição do YouTube do servidor'
            })
    )
    // Subcommand: role
    .addSubcommand(sub =>
        sub.setName('role')
            .setDescription('Change the role mentioned in notifications (empty = remove)')
            .setNameLocalizations({
                'es-ES': 'rol',
                'es-419': 'rol',
                'pt-BR': 'cargo'
            })
            .setDescriptionLocalizations({
                'es-ES': 'Cambia el rol a mencionar en las notificaciones (vacío = quitar)',
                'es-419': 'Cambia el rol a mencionar en las notificaciones (vacío = quitar)',
                'pt-BR': 'Muda o cargo mencionado nas notificações (vazio = remover)'
            })
            .addRoleOption(opt =>
                opt.setName('role')
                    .setDescription('Role to mention (leave empty to remove mention)')
                    .setNameLocalizations({
                        'es-ES': 'rol',
                        'es-419': 'rol',
                        'pt-BR': 'cargo'
                    })
                    .setDescriptionLocalizations({
                        'es-ES': 'Rol a mencionar (dejar vacío para quitar)',
                        'es-419': 'Rol a mencionar (dejar vacío para quitar)',
                        'pt-BR': 'Cargo a mencionar (deixe vazio para remover)'
                    })
            )
    )
    // Subcommand: channel
    .addSubcommand(sub =>
        sub.setName('channel')
            .setDescription('Change the Discord channel where notifications are sent')
            .setNameLocalizations({
                'es-ES': 'canal',
                'es-419': 'canal',
                'pt-BR': 'canal'
            })
            .setDescriptionLocalizations({
                'es-ES': 'Cambia el canal de Discord donde se notifica',
                'es-419': 'Cambia el canal de Discord donde se notifica',
                'pt-BR': 'Muda o canal do Discord onde se notifica'
            })
            .addChannelOption(opt =>
                opt.setName('channel')
                    .setDescription('Text channel to announce in')
                    .setNameLocalizations({
                        'es-ES': 'canal',
                        'es-419': 'canal',
                        'pt-BR': 'canal'
                    })
                    .setDescriptionLocalizations({
                        'es-ES': 'Canal de texto donde avisar',
                        'es-419': 'Canal de texto donde avisar',
                        'pt-BR': 'Canal de texto para avisar'
                    })
                    .addChannelTypes(ChannelType.GuildText)
                    .setRequired(true)
            )
    )
    // Subcommand: message
    .addSubcommand(sub =>
        sub.setName('message')
            .setDescription('Customize the notification message (placeholders available)')
            .setNameLocalizations({
                'es-ES': 'mensaje',
                'es-419': 'mensaje',
                'pt-BR': 'mensagem'
            })
            .setDescriptionLocalizations({
                'es-ES': 'Personaliza el mensaje de notificación (placeholders disponibles)',
                'es-419': 'Personaliza el mensaje de notificación (placeholders disponibles)',
                'pt-BR': 'Personaliza a mensagem de notificação (placeholders disponíveis)'
            })
            .addStringOption(opt =>
                opt.setName('message')
                    .setDescription('Custom template. Leave empty to reset to default.')
                    .setNameLocalizations({
                        'es-ES': 'mensaje',
                        'es-419': 'mensaje',
                        'pt-BR': 'mensagem'
                    })
                    .setDescriptionLocalizations({
                        'es-ES': 'Plantilla personalizada. Dejar vacío para restablecer.',
                        'es-419': 'Plantilla personalizada. Dejar vacío para restablecer.',
                        'pt-BR': 'Modelo personalizado. Deixe vazio para redefinir.'
                    })
            )
    );

export async function execute(interaction) {
    const subcommand = interaction.options.getSubcommand();
    const guildId = interaction.guildId;
    const member = interaction.member;

    const adminSubcommands = ['follow', 'unfollow', 'role', 'channel', 'message'];
    if (adminSubcommands.includes(subcommand)) {
        if (!member.permissions.has(PermissionFlagsBits.ManageGuild)) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'General:SinPermiso') || '❌ No tienes permisos para gestionar las notificaciones de YouTube.',
                ephemeral: true
            });
        }
    }

    if (subcommand === 'follow') {
        const canalInput = interaction.options.getString('channel');
        const notificarCanal = interaction.options.getChannel('notify');
        const rol = interaction.options.getRole('role');

        await interaction.deferReply();

        const resolved = await youtubeService.resolveChannel(canalInput);
        if (!resolved) {
            return interaction.editReply({
                content: MessagesService.get(guildId, 'YouTube:ErrorResolver')
            });
        }

        const existing = db.prepare('SELECT YTChannelId FROM YouTubeSubscriptions WHERE GuildId = ?').get(guildId);
        const isReplaced = !!existing;

        // Fetch latest video to backfill so past videos aren't announced
        const latest = await youtubeService.getLatestVideo(resolved.channelId);
        const lastVideoId = latest?.videoId || null;

        db.prepare(`
            INSERT INTO YouTubeSubscriptions (GuildId, YTChannelId, YTChannelName, NotifyChannelId, NotifyRoleId, LastVideoId, CustomMessage, CreatedAt)
            VALUES (?, ?, ?, ?, ?, ?, NULL, ?)
            ON CONFLICT(GuildId) DO UPDATE SET
                YTChannelId = excluded.YTChannelId,
                YTChannelName = excluded.YTChannelName,
                NotifyChannelId = excluded.NotifyChannelId,
                NotifyRoleId = excluded.NotifyRoleId,
                LastVideoId = excluded.LastVideoId
        `).run(
            guildId,
            resolved.channelId,
            resolved.channelName,
            notificarCanal.id,
            rol ? rol.id : null,
            lastVideoId,
            new Date().toISOString()
        );

        const key = isReplaced ? 'YouTube:SeguirReemplazado' : 'YouTube:SeguirExito';
        return interaction.editReply({
            content: MessagesService.get(guildId, key, {
                canal: resolved.channelName,
                destino: notificarCanal.toString()
            })
        });
    }

    if (subcommand === 'unfollow') {
        const res = db.prepare('DELETE FROM YouTubeSubscriptions WHERE GuildId = ?').run(guildId);
        if (res.changes === 0) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'YouTube:NoSuscrito'),
                ephemeral: true
            });
        }
        return interaction.reply({
            content: MessagesService.get(guildId, 'YouTube:Dejado')
        });
    }

    if (subcommand === 'show') {
        const sub = db.prepare(`
            SELECT YTChannelName, CAST(NotifyChannelId AS TEXT) as NotifyChannelId,
                   CAST(NotifyRoleId AS TEXT) as NotifyRoleId, CustomMessage
            FROM YouTubeSubscriptions WHERE GuildId = ?
        `).get(guildId);

        if (!sub) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'YouTube:VerSinSuscrito'),
                ephemeral: true
            });
        }

        const embed = new EmbedBuilder()
            .setTitle(MessagesService.get(guildId, 'YouTube:VerTitulo'))
            .setColor(0xFF0000)
            .addFields(
                { name: MessagesService.get(guildId, 'YouTube:VerCanal'), value: sub.YTChannelName, inline: true },
                { name: MessagesService.get(guildId, 'YouTube:VerDestino'), value: `<#${sub.NotifyChannelId}>`, inline: true },
                {
                    name: MessagesService.get(guildId, 'YouTube:VerRol'),
                    value: sub.NotifyRoleId ? `<@&${sub.NotifyRoleId}>` : MessagesService.get(guildId, 'YouTube:VerSinRol'),
                    inline: true
                },
                {
                    name: MessagesService.get(guildId, 'YouTube:VerPlantilla'),
                    value: sub.CustomMessage && sub.CustomMessage.trim()
                        ? `\`\`\`${sub.CustomMessage}\`\`\``
                        : MessagesService.get(guildId, 'YouTube:VerPorDefecto')
                }
            );

        return interaction.reply({ embeds: [embed] });
    }

    if (subcommand === 'role') {
        const rol = interaction.options.getRole('role');
        const sub = db.prepare('SELECT YTChannelId FROM YouTubeSubscriptions WHERE GuildId = ?').get(guildId);
        if (!sub) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'YouTube:NoSuscrito'),
                ephemeral: true
            });
        }

        db.prepare('UPDATE YouTubeSubscriptions SET NotifyRoleId = ? WHERE GuildId = ?')
            .run(rol ? rol.id : null, guildId);

        const text = rol
            ? MessagesService.get(guildId, 'YouTube:RolActualizado', { rol: rol.toString() })
            : MessagesService.get(guildId, 'YouTube:RolQuitado');

        return interaction.reply({ content: text });
    }

    if (subcommand === 'channel') {
        const canal = interaction.options.getChannel('channel');
        const sub = db.prepare('SELECT YTChannelId FROM YouTubeSubscriptions WHERE GuildId = ?').get(guildId);
        if (!sub) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'YouTube:NoSuscrito'),
                ephemeral: true
            });
        }

        db.prepare('UPDATE YouTubeSubscriptions SET NotifyChannelId = ? WHERE GuildId = ?')
            .run(canal.id, guildId);

        return interaction.reply({
            content: MessagesService.get(guildId, 'YouTube:CanalActualizado', { canal: canal.toString() })
        });
    }

    if (subcommand === 'message') {
        const mensaje = interaction.options.getString('message');
        const sub = db.prepare('SELECT YTChannelId FROM YouTubeSubscriptions WHERE GuildId = ?').get(guildId);
        if (!sub) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'YouTube:NoSuscrito'),
                ephemeral: true
            });
        }

        if (!mensaje || !mensaje.trim()) {
            db.prepare('UPDATE YouTubeSubscriptions SET CustomMessage = NULL WHERE GuildId = ?').run(guildId);
            return interaction.reply({
                content: MessagesService.get(guildId, 'YouTube:MensajeBorrado')
            });
        }

        db.prepare('UPDATE YouTubeSubscriptions SET CustomMessage = ? WHERE GuildId = ?').run(mensaje.trim(), guildId);

        const vista = MessagesService.get(guildId, 'YouTube:VistaPrevia');
        const opciones = MessagesService.get(guildId, 'YouTube:OpcionesPlantilla');

        return interaction.reply({
            content: MessagesService.get(guildId, 'YouTube:MensajeGuardado', { vista, opciones })
        });
    }
}

export default {
    data,
    execute
};
