import { AttachmentBuilder } from 'discord.js';
import { createCanvas, loadImage } from 'canvas';
import path from 'path';
import db from '../services/database.js';
import MessagesService from '../services/messagesService.js';
import fs from 'fs';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export async function handleGuildMemberAdd(member, client) {
    if (member.user.bot) return;

    const guildId = member.guild.id;
    const row = db.prepare(`SELECT WelcomeChannelId, WelcomeMessage FROM GuildConfigs WHERE GuildId = ?`).get(guildId);
    
    if (!row || !row.WelcomeChannelId) return;

    const channel = member.guild.channels.cache.get(row.WelcomeChannelId);
    if (!channel) return;

    const defaultMsg = MessagesService.get(guildId, 'Bienvenida:MensajePorDefecto', {
        usuario: member.toString(),
        servidor: member.guild.name
    });

    const messageContent = row.WelcomeMessage 
        ? row.WelcomeMessage
            .replace(/{usuario}|{user}/g, member.toString())
            .replace(/{servidor}|{server}/g, member.guild.name)
        : defaultMsg;

    try {
        const canvas = createCanvas(800, 350);
        const ctx = canvas.getContext('2d');

        // Draw background
        const bgPath = path.join(__dirname, '..', '..', '..', 'icon.jpg');
        if (fs.existsSync(bgPath)) {
            const background = await loadImage(bgPath);
            ctx.drawImage(background, 0, 0, canvas.width, canvas.height);
        } else {
            ctx.fillStyle = '#23272A';
            ctx.fillRect(0, 0, canvas.width, canvas.height);
        }

        // Add a dark overlay
        ctx.fillStyle = 'rgba(0, 0, 0, 0.6)';
        ctx.fillRect(0, 0, canvas.width, canvas.height);

        // Set text styles
        ctx.fillStyle = '#ffffff';
        ctx.textAlign = 'center';
        
        ctx.font = 'bold 50px sans-serif';
        ctx.fillText(MessagesService.get(guildId, 'Bienvenida:Titulo'), 400, 80);

        ctx.font = '35px sans-serif';
        const userName = member.user.username;
        ctx.fillText(userName, 400, 130);

        ctx.font = '25px sans-serif';
        ctx.fillText(`#${member.guild.memberCount}`, 400, 170);

        // Draw Avatar
        ctx.save();
        ctx.beginPath();
        ctx.arc(400, 260, 60, 0, Math.PI * 2, true);
        ctx.closePath();
        ctx.clip();

        const avatarUrl = member.user.displayAvatarURL({ extension: 'png', size: 128 });
        const avatar = await loadImage(avatarUrl);
        ctx.drawImage(avatar, 340, 200, 120, 120);
        ctx.restore();

        const attachment = new AttachmentBuilder(canvas.toBuffer('image/png'), { name: 'welcome-image.png' });

        await channel.send({ content: messageContent, files: [attachment] });
    } catch (error) {
        console.error('[bot] Error sending welcome message:', error);
    }
}
