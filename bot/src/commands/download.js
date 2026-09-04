import { SlashCommandBuilder, EmbedBuilder, AttachmentBuilder } from 'discord.js';
import { downloadMedia, YtDlpException } from '../services/download.js';
import { uploadToLitterbox } from '../services/litterbox.js';
import MessagesService from '../services/messagesService.js';
import db from '../services/database.js';
import fs from 'fs';
import path from 'path';

const MAX_DISCORD_BYTES = 9_437_184; // ~9 MiB

export const data = new SlashCommandBuilder()
    .setName('download')
    .setDescription('Download a video (or audio only) from the internet with yt-dlp')
    .addStringOption(option =>
        option.setName('url')
            .setDescription('URL of the content to download')
            .setRequired(true)
    )
    .addStringOption(option =>
        option.setName('format')
            .setDescription('What to download: video or audio only')
            .addChoices(
                { name: 'Video', value: 'video' },
                { name: 'Audio only', value: 'audio' }
            )
            .setRequired(false)
    );

export async function execute(interaction) {
    const guildId = interaction.guildId;
    
    // Check if downloads are enabled in guild settings
    const guildSettings = db.prepare('SELECT DownloadsEnabled FROM GuildConfigs WHERE GuildId = ?').get(guildId);
    if (guildSettings && guildSettings.DownloadsEnabled === 0) {
        return interaction.reply({
            content: MessagesService.get(guildId, 'Descargas:Desactivado'),
            ephemeral: true
        });
    }

    const url = interaction.options.getString('url');
    const format = interaction.options.getString('format') || 'video';
    const audioOnly = format === 'audio';

    if (!url.startsWith('http://') && !url.startsWith('https://')) {
        return interaction.reply({
            content: MessagesService.get(guildId, 'Descargas:UrlInvalida'),
            ephemeral: true
        });
    }

    await interaction.deferReply();

    let tempDir = null;

    try {
        const result = await downloadMedia(url, audioOnly, 4);
        tempDir = result.tempDir;

        const stat = fs.statSync(result.filePath);
        const size = stat.size;

        if (size <= MAX_DISCORD_BYTES) {
            // Attach directly to Discord message
            const attachment = new AttachmentBuilder(result.filePath, { name: path.basename(result.filePath) });
            await interaction.editReply({
                content: MessagesService.get(guildId, 'Descargas:Exito', { titulo: result.title }),
                files: [attachment]
            });
        } else {
            // Upload to litterbox and send embed link
            const publicUrl = await uploadToLitterbox(result.filePath, path.basename(result.filePath));
            const sizeMB = (size / (1024 * 1024)).toFixed(1);

            const embed = new EmbedBuilder()
                .setTitle(result.title)
                .setDescription(MessagesService.get(guildId, 'Descargas:DemasiadoGrandeEmbed', {
                    tamano: sizeMB,
                    enlace: publicUrl
                }))
                .setURL(publicUrl)
                .setColor(0x00A8FF)
                .setFooter({ text: MessagesService.get(guildId, 'Descargas:PieLitterbox') });

            await interaction.editReply({ embeds: [embed] });
        }
    } catch (error) {
        console.error('[download] Error:', error);
        if (error instanceof YtDlpException) {
            await interaction.editReply({
                content: MessagesService.get(guildId, 'Descargas:Error', { detalles: error.message })
            });
        } else {
            await interaction.editReply({
                content: MessagesService.get(guildId, 'Descargas:ErrorGenerico')
            });
        }
    } finally {
        if (tempDir && fs.existsSync(tempDir)) {
            try {
                fs.rmSync(tempDir, { recursive: true, force: true });
            } catch {}
        }
    }
}
