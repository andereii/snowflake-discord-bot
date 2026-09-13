import { EmbedBuilder, AttachmentBuilder } from 'discord.js';
import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import { downloadMedia, YtDlpException } from '../../services/download.js';
import { uploadToLitterbox } from '../../services/litterbox.js';
import { getGuildConfig, isEnabled } from '../../services/guildConfig.js';
import MessagesService from '../../services/messagesService.js';
import fs from 'fs';
import path from 'path';

const MAX_DISCORD_BYTES = 9_437_184;

export const data = slash('download', 'Download a video (or audio only) from the internet with yt-dlp', {
    names: esPt('descargar', 'baixar'),
    descriptions: esPt(
        'Descarga un vídeo (o solo audio) de Internet con yt-dlp',
        'Baixa um vídeo (ou só o áudio) da internet com yt-dlp'
    )
})
    .addStringOption(o => o.setName('url').setDescription('URL of the content to download').setRequired(true))
    .addStringOption(o => o.setName('format').setDescription('Video or audio only')
        .addChoices({ name: 'Video', value: 'video' }, { name: 'Audio only', value: 'audio' }));

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const cfg = getGuildConfig(guildId);
    if (!isEnabled(cfg, 'DownloadsEnabled', true)) {
        return interaction.reply({ content: MessagesService.get(guildId, 'Descargas:Desactivado'), ephemeral: true });
    }

    const url = interaction.options.getString('url');
    const audioOnly = (interaction.options.getString('format') || 'video') === 'audio';

    if (!url.startsWith('http://') && !url.startsWith('https://')) {
        return interaction.reply({ content: MessagesService.get(guildId, 'Descargas:UrlInvalida'), ephemeral: true });
    }

    await interaction.deferReply();
    let tempDir = null;

    try {
        const result = await downloadMedia(url, audioOnly, 4);
        tempDir = result.tempDir;
        const size = fs.statSync(result.filePath).size;

        if (size <= MAX_DISCORD_BYTES) {
            const attachment = new AttachmentBuilder(result.filePath, { name: path.basename(result.filePath) });
            await interaction.editReply({
                content: MessagesService.get(guildId, 'Descargas:Exito', { titulo: result.title }),
                files: [attachment]
            });
            return;
        }

        const publicUrl = await uploadToLitterbox(result.filePath, path.basename(result.filePath));
        await interaction.editReply({
            embeds: [
                new EmbedBuilder()
                    .setTitle(result.title)
                    .setDescription(MessagesService.get(guildId, 'Descargas:DemasiadoGrandeEmbed', {
                        tamano: (size / (1024 * 1024)).toFixed(1),
                        enlace: publicUrl
                    }))
                    .setURL(publicUrl)
                    .setColor(0x00A8FF)
                    .setFooter({ text: MessagesService.get(guildId, 'Descargas:PieLitterbox') })
            ]
        });
    } catch (error) {
        console.error('[download]', error);
        const key = error instanceof YtDlpException ? 'Descargas:Error' : 'Descargas:ErrorGenerico';
        const extras = error instanceof YtDlpException ? { detalles: error.message } : {};
        await interaction.editReply({ content: MessagesService.get(guildId, key, extras) });
    } finally {
        if (tempDir && fs.existsSync(tempDir)) {
            try { fs.rmSync(tempDir, { recursive: true, force: true }); } catch { /* ignore */ }
        }
    }
}
