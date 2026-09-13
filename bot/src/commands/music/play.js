import { EmbedBuilder } from 'discord.js';
import path from 'path';
import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import {
    ensureQueue,
    playNext,
    resolveQuery,
    songVars,
    AUDIO_EXTENSIONS
} from '../../services/music.js';
import { sendOrUpdateWidget } from '../../services/musicWidget.js';
import MessagesService from '../../services/messagesService.js';

export const data = slash('play', 'Play a song, playlist, or attached file', {
    names: esPt('reproducir', 'tocar'),
    descriptions: esPt(
        'Reproduce una canción, playlist o archivo subido',
        'Toca uma música, playlist ou arquivo enviado'
    )
})
    .addStringOption(option =>
        option.setName('query').setDescription('URL or search query').setRequired(false)
    )
    .addAttachmentOption(option =>
        option.setName('file').setDescription('Audio file to play').setRequired(false)
    );

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const query = interaction.options.getString('query');
    const attachment = interaction.options.getAttachment('file');
    const voiceChannel = interaction.member.voice.channel;

    if (!voiceChannel) {
        return interaction.reply({ content: MessagesService.get(guildId, 'Musica:NoEnCanal'), ephemeral: true });
    }
    if (!query && !attachment) {
        return interaction.reply({ content: MessagesService.get(guildId, 'Musica:NoConsulta'), ephemeral: true });
    }

    await interaction.deferReply();

    let songs = [];
    let playlistTitle = null;

    if (attachment) {
        const ext = path.extname(attachment.name || '').toLowerCase();
        const mimeOk = Boolean(attachment.contentType?.startsWith('audio/'));
        if (!mimeOk && !AUDIO_EXTENSIONS.includes(ext)) {
            return interaction.editReply({ content: MessagesService.get(guildId, 'Musica:ArchivoInvalido') });
        }
        songs = [{
            title: attachment.name,
            url: attachment.url,
            duration: null,
            thumbnail: null,
            author: interaction.user.globalName || interaction.user.username,
            isLive: false,
            isAttachment: true
        }];
    } else {
        const result = await resolveQuery(query);
        songs = result.songs;
        playlistTitle = result.playlistTitle;
    }

    if (!songs.length) {
        return interaction.editReply({ content: MessagesService.get(guildId, 'Musica:NoEncontrado') });
    }

    let queue;
    try {
        queue = await ensureQueue(interaction);
    } catch (err) {
        console.error('[play] voice join failed:', err.message);
        return interaction.editReply({ content: MessagesService.get(guildId, 'Musica:ErrorLavalink') });
    }

    const requester = interaction.user.tag;
    const startedIdle = !queue.playing && queue.songs.length === 0;
    const first = songs[0];
    queue.songs.push(...songs.map(song => ({ ...song, requester })));

    if (playlistTitle) {
        await interaction.editReply({
            content: MessagesService.get(guildId, 'Musica:PlaylistAnadida', {
                titulo: playlistTitle,
                n: songs.length
            })
        });
    } else if (!startedIdle) {
        const vars = songVars(first, guildId);
        const embed = new EmbedBuilder()
            .setColor('Blurple')
            .setDescription(MessagesService.get(guildId, 'Musica:PuestaEnCola', vars));
        if (first.thumbnail) embed.setThumbnail(first.thumbnail);
        await interaction.editReply({ embeds: [embed] });
    } else {
        await interaction.editReply({
            content: MessagesService.get(guildId, 'Musica:Tocando', songVars(first, guildId))
        });
    }

    if (startedIdle) await playNext(guildId);
    await sendOrUpdateWidget(interaction.channel, guildId).catch(() => {});
}
