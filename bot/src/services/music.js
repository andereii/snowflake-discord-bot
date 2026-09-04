import { joinVoiceChannel, createAudioPlayer, createAudioResource, AudioPlayerStatus, getVoiceConnection } from '@discordjs/voice';
import ytdl from 'ytdl-core';
import ytSearch from 'yt-search';
import { EmbedBuilder } from 'discord.js';

const queues = new Map();

export function getQueue(guildId) {
    return queues.get(guildId);
}

export function createQueue(guildId, voiceChannel, textChannel) {
    const queue = {
        voiceChannel,
        textChannel,
        connection: null,
        player: createAudioPlayer(),
        songs: [],
        playing: false,
        volume: 1.0,
    };

    queue.player.on(AudioPlayerStatus.Idle, () => {
        queue.songs.shift();
        playNext(guildId);
    });

    queue.player.on('error', error => {
        console.error('AudioPlayer Error:', error.message);
        queue.songs.shift();
        playNext(guildId);
    });

    queues.set(guildId, queue);
    return queue;
}

export function deleteQueue(guildId) {
    queues.delete(guildId);
}

export async function playNext(guildId) {
    const queue = getQueue(guildId);
    if (!queue) return;

    if (queue.songs.length === 0) {
        if (queue.connection) {
            queue.connection.destroy();
        }
        deleteQueue(guildId);
        queue.textChannel.send('La cola de reproducción ha terminado. Desconectando...').catch(console.error);
        return;
    }

    const song = queue.songs[0];
    try {
        let resource;
        if (song.isAttachment) {
            resource = createAudioResource(song.url, { inlineVolume: true });
        } else {
            const stream = ytdl(song.url, { filter: 'audioonly', quality: 'highestaudio', highWaterMark: 1 << 25 });
            resource = createAudioResource(stream, { inlineVolume: true });
        }
        resource.volume.setVolume(queue.volume);
        
        queue.player.play(resource);
        queue.playing = true;

        const embed = new EmbedBuilder()
            .setColor('Blurple')
            .setTitle('🎶 Reproduciendo ahora')
            .setDescription(`**[${song.title}](${song.url})**\nSolicitado por: ${song.requester}`);
        
        if (song.thumbnail) embed.setThumbnail(song.thumbnail);

        queue.textChannel.send({ embeds: [embed] }).catch(console.error);
    } catch (error) {
        console.error('Error playing song:', error);
        queue.textChannel.send('Hubo un error al intentar reproducir la canción.').catch(console.error);
        queue.songs.shift();
        playNext(guildId);
    }
}

export async function searchSong(query) {
    // Si es una URL de youtube
    if (ytdl.validateURL(query)) {
        try {
            const info = await ytdl.getInfo(query);
            return {
                title: info.videoDetails.title,
                url: info.videoDetails.video_url,
                duration: info.videoDetails.lengthSeconds,
                thumbnail: info.videoDetails.thumbnails.length > 0 ? info.videoDetails.thumbnails[0].url : null
            };
        } catch (e) {
            console.error('Error info youtube', e);
            return null;
        }
    }
    
    // Buscar con yt-search
    try {
        const r = await ytSearch(query);
        const videos = r.videos;
        if (videos.length > 0) {
            return {
                title: videos[0].title,
                url: videos[0].url,
                duration: videos[0].seconds,
                thumbnail: videos[0].thumbnail
            };
        }
    } catch (e) {
        console.error('Error yt-search', e);
    }
    return null;
}
