import axios from 'axios';
import { ActionRowBuilder, ButtonBuilder, ButtonStyle, EmbedBuilder, PermissionFlagsBits } from 'discord.js';
import MessagesService from './messagesService.js';

export const BTN_PREV = 'img_prev';
export const BTN_NEXT = 'img_next';
export const BTN_DEL = 'img_del';

const BUTTON_IDS = [BTN_PREV, BTN_NEXT, BTN_DEL];

export function isImageWidgetInteraction(customId) {
    return BUTTON_IDS.includes(customId);
}

/**
 * MessageId -> { userId: string, query: string, urls: string[], index: number }
 */
const sessions = new Map();

/**
 * Search Bing and fallback engines for high quality images
 * @param {string} query
 * @returns {Promise<string[]>}
 */
export async function searchImages(query) {
    // 1. Try Bing async image search (most reliable & high resolution)
    try {
        const headers = {
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36',
            'Accept-Language': 'en-US,en;q=0.9',
            'Accept': 'text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8'
        };

        const res = await axios.get(`https://www.bing.com/images/async?q=${encodeURIComponent(query)}&first=0&count=35&mmasync=1`, {
            headers,
            timeout: 8000
        });

        const urls = [];
        const mMatches = [...res.data.matchAll(/m=\"({[^\"]+})\"/g)];
        for (const m of mMatches) {
            try {
                const unescaped = m[1].replace(/&quot;/g, '"');
                const data = JSON.parse(unescaped);
                if (data.murl && typeof data.murl === 'string' && data.murl.startsWith('http')) {
                    urls.push(data.murl);
                }
            } catch {}
        }

        if (urls.length > 0) {
            return urls;
        }
    } catch (err) {
        console.warn('[imageSearch] Bing search failed, attempting fallback...', err.message);
    }

    // 2. Fallback: DuckDuckGo image search
    try {
        const headers = {
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36',
            'Accept-Language': 'en-US,en;q=0.9',
            'Referer': 'https://duckduckgo.com/'
        };

        const res1 = await axios.get(`https://duckduckgo.com/?q=${encodeURIComponent(query)}&t=h_&iax=images&ia=images`, {
            headers,
            timeout: 6000
        });

        let vqdMatch = res1.data.match(/vqd="([^"]+)"/);
        if (!vqdMatch) vqdMatch = res1.data.match(/vqd=([\d-]+)/);

        if (vqdMatch) {
            const vqd = vqdMatch[1];
            const res2 = await axios.get(`https://duckduckgo.com/i.js?l=us-en&o=json&q=${encodeURIComponent(query)}&vqd=${vqd}&f=,,,,,&p=1`, {
                headers,
                timeout: 6000
            });

            const results = res2.data?.results || [];
            const urls = [];
            for (const item of results) {
                if (item.image && typeof item.image === 'string' && item.image.startsWith('http')) {
                    urls.push(item.image);
                }
            }
            if (urls.length > 0) return urls;
        }
    } catch (err) {
        console.error('[imageSearch] Fallback failed for query:', query, err.message);
    }

    return [];
}

export function registerSession(messageId, userId, query, urls) {
    sessions.set(messageId, {
        userId,
        query,
        urls,
        index: 0
    });
}

export function buildEmbed(query, urls, index) {
    return new EmbedBuilder()
        .setTitle(`🔎 ${query}`)
        .setImage(urls[index])
        .setFooter({ text: `${index + 1} / ${urls.length}` })
        .setColor(0x00A8FF);
}

export function buildButtons() {
    return new ActionRowBuilder().addComponents(
        new ButtonBuilder()
            .setCustomId(BTN_PREV)
            .setLabel('◀️')
            .setStyle(ButtonStyle.Primary),
        new ButtonBuilder()
            .setCustomId(BTN_DEL)
            .setLabel('🗑️')
            .setStyle(ButtonStyle.Danger),
        new ButtonBuilder()
            .setCustomId(BTN_NEXT)
            .setLabel('▶️')
            .setStyle(ButtonStyle.Primary)
    );
}

export async function handleButtonInteraction(interaction) {
    const messageId = interaction.message.id;
    const session = sessions.get(messageId);
    const guildId = interaction.guildId;
    const buttonId = interaction.customId;

    if (!session) {
        return interaction.deferUpdate().catch(() => {});
    }

    if (interaction.user.id !== session.userId && buttonId !== BTN_DEL) {
        return interaction.reply({
            content: MessagesService.get(guildId, 'Errores:NoEresAutor'),
            ephemeral: true
        });
    }

    if (buttonId === BTN_DEL) {
        if (interaction.user.id !== session.userId) {
            const member = interaction.member;
            const canManage = member?.permissions?.has(PermissionFlagsBits.ManageMessages);
            if (!canManage) {
                return interaction.reply({
                    content: MessagesService.get(guildId, 'Errores:SinPermisos'),
                    ephemeral: true
                });
            }
        }

        sessions.delete(messageId);
        await interaction.message.delete().catch(() => {});
        return;
    }

    if (buttonId === BTN_NEXT) {
        session.index++;
        if (session.index >= session.urls.length) session.index = 0;
    } else if (buttonId === BTN_PREV) {
        session.index--;
        if (session.index < 0) session.index = session.urls.length - 1;
    }

    const embed = buildEmbed(session.query, session.urls, session.index);
    const row = buildButtons();

    await interaction.update({
        embeds: [embed],
        components: [row]
    });
}

export default {
    searchImages,
    registerSession,
    buildEmbed,
    buildButtons,
    handleButtonInteraction,
    isImageWidgetInteraction
};
