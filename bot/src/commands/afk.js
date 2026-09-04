import { SlashCommandBuilder, EmbedBuilder } from 'discord.js';
import afkService from '../services/afk.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
    .setName('afk')
    .setDescription('Set your AFK status')
    .addStringOption(option => 
        option.setName('message')
            .setDescription('Reason for being AFK')
            .setRequired(false)
    );

export async function execute(interaction) {
    const guildId = interaction.guild.id;
    const mensaje = interaction.options.getString('message');
    const motivoLimpio = (!mensaje || mensaje.trim() === '') ? 'AFK' : mensaje.trim().substring(0, 250);
    const userId = interaction.user.id;
    const member = interaction.member;

    let originalNickname = null;
    if (member && member.nickname) {
        originalNickname = member.nickname;
    }

    afkService.setAfk(guildId, userId, motivoLimpio, originalNickname);

    if (member && member.manageable) {
        try {
            const currentNick = member.nickname || interaction.user.username;
            if (!currentNick.startsWith('[AFK] ')) {
                let newNick = `[AFK] ${currentNick}`;
                if (newNick.length > 32) newNick = newNick.substring(0, 32);
                await member.setNickname(newNick);
            }
        } catch {
            // Ignore hierarchy failures
        }
    }

    const desc = MessagesService.get(guildId, 'Afk:Establecido', { motivo: `**${motivoLimpio}**` });

    const embed = new EmbedBuilder()
        .setTitle(interaction.user.username)
        .setThumbnail(interaction.user.displayAvatarURL())
        .setDescription(desc)
        .setColor(0x6495ED);

    await interaction.reply({ embeds: [embed] });
}
