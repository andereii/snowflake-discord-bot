import { AttachmentBuilder } from 'discord.js';
import { createCanvas, loadImage } from 'canvas';
import path from 'path';
import fs from 'fs';
import { fileURLToPath } from 'url';
import { getGuildConfig } from '../services/guildConfig.js';
import MessagesService from '../services/messagesService.js';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export async function handleGuildMemberAdd(member) {
    if (member.user.bot) return;

    const guildId = member.guild.id;
    const row = getGuildConfig(guildId);
    if (!row?.WelcomeChannelId) return;

    const channel = member.guild.channels.cache.get(row.WelcomeChannelId);
    if (!channel) return;

    const defaultMsg = MessagesService.get(guildId, 'Bienvenida:MensajePorDefecto', {
        usuario: member.toString(),
        servidor: member.guild.name
    });
    const content = row.WelcomeMessage
        ? row.WelcomeMessage
            .replace(/{usuario}|{user}/g, member.toString())
            .replace(/{servidor}|{server}/g, member.guild.name)
        : defaultMsg;

    try {
        const canvas = createCanvas(800, 350);
        const ctx = canvas.getContext('2d');
        const bgPath = path.join(__dirname, '..', '..', '..', 'icon.jpg');

        if (fs.existsSync(bgPath)) {
            ctx.drawImage(await loadImage(bgPath), 0, 0, canvas.width, canvas.height);
        } else {
            ctx.fillStyle = '#23272A';
            ctx.fillRect(0, 0, canvas.width, canvas.height);
        }

        ctx.fillStyle = 'rgba(0, 0, 0, 0.6)';
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        ctx.fillStyle = '#ffffff';
        ctx.textAlign = 'center';
        ctx.font = 'bold 50px sans-serif';
        ctx.fillText(MessagesService.get(guildId, 'Bienvenida:Titulo'), 400, 80);
        ctx.font = '35px sans-serif';
        ctx.fillText(member.user.username, 400, 130);
        ctx.font = '25px sans-serif';
        ctx.fillText(`#${member.guild.memberCount}`, 400, 170);

        ctx.save();
        ctx.beginPath();
        ctx.arc(400, 260, 60, 0, Math.PI * 2, true);
        ctx.closePath();
        ctx.clip();
        const avatar = await loadImage(member.user.displayAvatarURL({ extension: 'png', size: 128 }));
        ctx.drawImage(avatar, 340, 200, 120, 120);
        ctx.restore();

        const attachment = new AttachmentBuilder(canvas.toBuffer('image/png'), { name: 'welcome-image.png' });
        await channel.send({ content, files: [attachment] });
    } catch (error) {
        console.error('[welcome]', error);
    }
}
