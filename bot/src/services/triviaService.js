import { ActionRowBuilder, ButtonBuilder, ButtonStyle, EmbedBuilder } from 'discord.js';
import axios from 'axios';
import db from './database.js';
import MessagesService from './messagesService.js';

const activeSessions = new Map();
const questionCache = new Map();
let lastApiCallTime = 0;

export const CATEGORIES = {
    anime: { id: 31, name: 'Anime & Manga' },
    general: { id: 9, name: 'General Knowledge' },
    videogames: { id: 15, name: 'Video Games' },
    film: { id: 11, name: 'Film & Cinema' },
    music: { id: 12, name: 'Music' },
    television: { id: 14, name: 'Television' },
    books: { id: 10, name: 'Books & Literature' },
    comics: { id: 29, name: 'Comics' },
    cartoons: { id: 32, name: 'Cartoons & Animation' },
    science: { id: 17, name: 'Science & Nature' },
    computers: { id: 18, name: 'Computers & Tech' },
    mythology: { id: 20, name: 'Mythology' },
    sports: { id: 21, name: 'Sports' },
    geography: { id: 22, name: 'Geography' },
    history: { id: 23, name: 'History' }
};

export function decodeHtml(html) {
    if (!html) return '';
    return html
        .replace(/&amp;/g, '&')
        .replace(/&quot;/g, '"')
        .replace(/&#039;/g, "'")
        .replace(/&apos;/g, "'")
        .replace(/&lt;/g, '<')
        .replace(/&gt;/g, '>')
        .replace(/&ldquo;/g, '"')
        .replace(/&rdquo;/g, '"')
        .replace(/&lsquo;/g, "'")
        .replace(/&rsquo;/g, "'")
        .replace(/&#(\d+);/g, (_, code) => String.fromCharCode(code))
        .replace(/&#x([0-9a-fA-F]+);/g, (_, code) => String.fromCharCode(parseInt(code, 16)));
}

/**
 * Intelligently extract search terms from a trivia question to find a relevant GIF
 */
export function extractSmartGifQuery(question, categoryKey = 'anime') {
    const q = decodeHtml(question);
    let tag = ' anime';
    if (categoryKey === 'videogames') tag = ' game';
    else if (categoryKey === 'film' || categoryKey === 'television') tag = ' movie';
    else if (categoryKey === 'cartoons') tag = ' cartoon';
    else if (categoryKey !== 'anime') tag = '';

    // 1. Quoted terms (highest priority, e.g. "Guilty Crown", "Death Note")
    const quoteMatch = q.match(/["“]([^"”]+)["”]/) || q.match(/['‘]([^'’]+)['’]/);
    if (quoteMatch && quoteMatch[1].length > 2 && !['true', 'false'].includes(quoteMatch[1].toLowerCase())) {
        return quoteMatch[1].trim() + tag;
    }

    // 2. "In [the anime] <Title>," or "In <Title>,"
    const inMatch = q.match(/In\s+(?:the\s+(?:anime|manga|series|movie|show|game|cartoon)\s+)?([A-Z][a-zA-Z0-9'\s:!?-]+?),/i);
    if (inMatch && inMatch[1].length > 2) {
        return inMatch[1].trim() + tag;
    }

    // 3. Extract proper nouns (capitalized keywords excluding question words)
    const words = q.replace(/[^a-zA-Z0-9'\s]/g, ' ').split(/\s+/);
    const stopWords = new Set([
        'What', 'Who', 'Which', 'Where', 'When', 'Why', 'How',
        'In', 'The', 'Is', 'Are', 'Was', 'Were', 'Did', 'Does', 'Do',
        'Of', 'From', 'To', 'And', 'Or', 'A', 'An', 'This', 'That',
        'These', 'Those', 'Name', 'According', 'Following', 'Main',
        'Character', 'Author', 'Creator', 'Year', 'Episode', 'Episodes',
        'Season', 'Manga', 'Anime', 'Series'
    ]);

    const properNouns = [];
    for (let i = 0; i < words.length; i++) {
        const w = words[i];
        if (w && w[0] === w[0].toUpperCase() && !stopWords.has(w) && w.length > 1) {
            properNouns.push(w);
        }
    }

    if (properNouns.length > 0) {
        return properNouns.slice(0, 3).join(' ') + tag;
    }

    return (categoryKey === 'anime' ? 'anime ' : '') + q.slice(0, 30);
}

/**
 * Search Bing for an animated GIF
 */
export async function searchGif(query) {
    try {
        const headers = {
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36',
            'Accept': 'text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8'
        };
        const url = `https://www.bing.com/images/async?q=${encodeURIComponent(query + ' gif')}&first=0&count=15&mmasync=1&qft=+filterui:photo-animatedgif`;
        const res = await axios.get(url, { headers, timeout: 5000 });

        const mMatches = [...res.data.matchAll(/m="({[^"]+})"/g)];
        for (const m of mMatches) {
            try {
                const data = JSON.parse(m[1].replace(/&quot;/g, '"'));
                if (data.murl && (data.murl.endsWith('.gif') || data.murl.includes('.gif?'))) {
                    return data.murl;
                }
            } catch {}
        }

        if (mMatches.length > 0) {
            try {
                const data = JSON.parse(mMatches[0][1].replace(/&quot;/g, '"'));
                if (data.murl) return data.murl;
            } catch {}
        }
    } catch (e) {
        // Fallback silently if GIF search fails
    }
    return null;
}

/**
 * Fetch a batch of questions from Open Trivia DB with rate-limit protection
 */
async function refillQuestionCache(catId, difficulty = null) {
    const now = Date.now();
    const timeSinceLast = now - lastApiCallTime;
    if (timeSinceLast < 5100) {
        await new Promise(r => setTimeout(r, 5100 - timeSinceLast));
    }

    let url = `https://opentdb.com/api.php?amount=10&type=multiple&category=${catId}`;
    if (difficulty && ['easy', 'medium', 'hard'].includes(difficulty.toLowerCase())) {
        url += `&difficulty=${difficulty.toLowerCase()}`;
    }

    lastApiCallTime = Date.now();
    const res = await axios.get(url, { timeout: 8000 });

    if (!res.data || res.data.response_code !== 0 || !res.data.results || res.data.results.length === 0) {
        throw new Error(`OpenTDB error: response_code ${res.data?.response_code}`);
    }

    return res.data.results;
}

/**
 * Fetch a question from Open Trivia DB and find a matching GIF
 */
export async function fetchOpenTdbQuestion(categoryKey = 'anime', difficulty = null) {
    const cat = CATEGORIES[categoryKey] || CATEGORIES.anime;
    const catId = cat.id;
    const cacheKey = `${categoryKey}_${difficulty || 'all'}`;

    if (!questionCache.has(cacheKey)) {
        questionCache.set(cacheKey, []);
    }

    const queue = questionCache.get(cacheKey);

    let item;
    if (queue.length > 0) {
        item = queue.shift();
    } else {
        const batch = await refillQuestionCache(catId, difficulty);
        item = batch.shift();
        queue.push(...batch);
    }

    const pregunta = decodeHtml(item.question);
    const correctText = decodeHtml(item.correct_answer);
    const incorrects = (item.incorrect_answers || []).map(decodeHtml);

    // Combine and shuffle options
    const allOptions = [correctText, ...incorrects];
    for (let i = allOptions.length - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1));
        [allOptions[i], allOptions[j]] = [allOptions[j], allOptions[i]];
    }

    const correctIndex = allOptions.indexOf(correctText);

    // Points and difficulty label
    let puntos = 10;
    let diffName = 'Fácil';
    if (item.difficulty === 'medium') {
        puntos = 20;
        diffName = 'Media';
    } else if (item.difficulty === 'hard') {
        puntos = 30;
        diffName = 'Difícil';
    }

    // Smart GIF query search
    const gifQuery = extractSmartGifQuery(pregunta, categoryKey);
    const gifUrl = await searchGif(gifQuery);

    return {
        pregunta,
        categoriaNombre: cat.name,
        dificultadNombre: diffName,
        puntos,
        opciones: allOptions,
        correctIndex,
        correctText,
        gifUrl
    };
}

export function isTriviaInteraction(customId) {
    return typeof customId === 'string' && customId.startsWith('trivia_ans_');
}

export async function handleTriviaButton(interaction) {
    const parts = interaction.customId.split('_');
    if (parts.length < 4) return;

    const sessionId = parts[2];
    const optionIndex = parseInt(parts[3], 10);

    const session = activeSessions.get(sessionId);
    if (!session) {
        return interaction.reply({
            content: '⚠️ Esta trivia ya ha finalizado.',
            ephemeral: true
        });
    }

    if (interaction.user.id !== session.userId) {
        return interaction.reply({
            content: '❌ Solo el jugador que inició esta trivia puede responder.',
            ephemeral: true
        });
    }

    activeSessions.delete(sessionId);
    if (session.timeoutTimer) clearTimeout(session.timeoutTimer);

    await finishRound(interaction, session, optionIndex);
}

export async function startTriviaRound(interaction, categoria = null, dificultad = null) {
    const guildId = interaction.guildId;
    const catKey = categoria && CATEGORIES[categoria] ? categoria : 'anime';

    await interaction.deferReply();

    let question;
    try {
        question = await fetchOpenTdbQuestion(catKey, dificultad);
    } catch (err) {
        console.error('[trivia] Failed to fetch OpenTDB question:', err.message);
        return interaction.editReply({
            content: '❌ No se pudo conectar con la API de Open Trivia DB en este momento. Inténtalo de nuevo en unos segundos.'
        });
    }

    const sessionId = Math.random().toString(36).substring(2, 10);

    const embed = new EmbedBuilder()
        .setTitle(`❓ ${MessagesService.get(guildId, 'Trivia:Titulo') || 'Pregunta de Trivia'}`)
        .setDescription(`### ${question.pregunta}`)
        .setColor(0x3498DB)
        .addFields(
            { name: '📚 Categoría', value: question.categoriaNombre, inline: true },
            { name: '⚡ Dificultad', value: `${question.dificultadNombre} (+${question.puntos} pts)`, inline: true }
        )
        .setFooter({ text: `Jugador: ${interaction.user.username} • 25s para responder` });

    if (question.gifUrl) {
        embed.setImage(question.gifUrl);
    }

    const rows = [];
    const letters = ['A', 'B', 'C', 'D'];
    const row1 = new ActionRowBuilder();
    const row2 = new ActionRowBuilder();

    question.opciones.forEach((opt, idx) => {
        const btn = new ButtonBuilder()
            .setCustomId(`trivia_ans_${sessionId}_${idx}`)
            .setLabel(`${letters[idx]}) ${opt.slice(0, 75)}`)
            .setStyle(ButtonStyle.Primary);

        if (idx < 2) row1.addComponents(btn);
        else row2.addComponents(btn);
    });

    rows.push(row1);
    if (row2.components.length > 0) rows.push(row2);

    const message = await interaction.editReply({ embeds: [embed], components: rows });

    const session = {
        sessionId,
        guildId,
        channelId: interaction.channelId,
        userId: interaction.user.id,
        userTag: interaction.user.username,
        question,
        message,
        interaction,
        timeoutTimer: null
    };

    // Auto timeout after 25 seconds
    session.timeoutTimer = setTimeout(async () => {
        if (activeSessions.has(sessionId)) {
            activeSessions.delete(sessionId);
            await handleTimeout(session);
        }
    }, 25000);

    activeSessions.set(sessionId, session);
}

async function finishRound(interaction, session, chosenIndex) {
    const isCorrect = chosenIndex === session.question.correctIndex;
    const guildId = session.guildId;
    const userId = session.userId;

    // Update stats in SQLite
    const stat = getOrCreateStats(guildId, userId);
    stat.TotalAnswers++;

    let resultColor = 0xE74C3C; // Red
    let resultTitle = `❌ ¡Incorrecto!`;
    let resultDesc = `La respuesta correcta era: **${session.question.correctText}**\n\n`;

    if (isCorrect) {
        stat.CorrectAnswers++;
        stat.Score += session.question.puntos;
        stat.CurrentStreak++;
        if (stat.CurrentStreak > stat.BestStreak) {
            stat.BestStreak = stat.CurrentStreak;
        }

        resultColor = 0x2ECC71; // Green
        resultTitle = `✅ ¡Respuesta Correcta! (+${session.question.puntos} pts)`;
        resultDesc = `¡Bien hecho! Tu racha actual es de **${stat.CurrentStreak}** seguidas 🔥\n\n`;
    } else {
        stat.CurrentStreak = 0;
    }

    saveStats(stat);

    const embed = new EmbedBuilder()
        .setTitle(resultTitle)
        .setDescription(resultDesc + `**Pregunta:** ${session.question.pregunta}`)
        .setColor(resultColor)
        .addFields(
            { name: '⭐ Puntuación Total', value: `\`${stat.Score}\` pts`, inline: true },
            { name: '🔥 Racha', value: `\`${stat.CurrentStreak}\` (Mejor: \`${stat.BestStreak}\`)`, inline: true },
            { name: '🎯 Aciertos', value: `\`${stat.CorrectAnswers}/${stat.TotalAnswers}\``, inline: true }
        )
        .setFooter({ text: `Snowflake Trivia • ${interaction.guild?.name || 'Discord'}` });

    if (session.question.gifUrl) {
        embed.setImage(session.question.gifUrl);
    }

    const disabledRows = buildResultButtons(session.question, chosenIndex);
    await interaction.update({ embeds: [embed], components: disabledRows });
}

async function handleTimeout(session) {
    const guildId = session.guildId;
    const stat = getOrCreateStats(guildId, session.userId);
    stat.TotalAnswers++;
    stat.CurrentStreak = 0;
    saveStats(stat);

    const embed = new EmbedBuilder()
        .setTitle(`⏰ ¡Tiempo agotado!`)
        .setDescription(`Se acabaron los 25 segundos.\nLa respuesta correcta era: **${session.question.correctText}**\n\n**Pregunta:** ${session.question.pregunta}`)
        .setColor(0xE74C3C)
        .addFields(
            { name: '⭐ Puntuación', value: `\`${stat.Score}\` pts`, inline: true },
            { name: '🎯 Aciertos', value: `\`${stat.CorrectAnswers}/${stat.TotalAnswers}\``, inline: true }
        )
        .setFooter({ text: `Snowflake Trivia` });

    if (session.question.gifUrl) {
        embed.setImage(session.question.gifUrl);
    }

    const disabledRows = buildResultButtons(session.question, -1);

    if (session.message && session.message.edit) {
        await session.message.edit({ embeds: [embed], components: disabledRows }).catch(() => {});
    }
}

function buildResultButtons(question, chosenIndex) {
    const letters = ['A', 'B', 'C', 'D'];
    const row1 = new ActionRowBuilder();
    const row2 = new ActionRowBuilder();

    question.opciones.forEach((opt, idx) => {
        let style = ButtonStyle.Secondary;
        if (idx === question.correctIndex) {
            style = ButtonStyle.Success;
        } else if (idx === chosenIndex) {
            style = ButtonStyle.Danger;
        }

        const btn = new ButtonBuilder()
            .setCustomId(`trivia_ans_ended_${idx}`)
            .setLabel(`${letters[idx]}) ${opt.slice(0, 75)}`)
            .setStyle(style)
            .setDisabled(true);

        if (idx < 2) row1.addComponents(btn);
        else row2.addComponents(btn);
    });

    const rows = [row1];
    if (row2.components.length > 0) rows.push(row2);
    return rows;
}

function getOrCreateStats(guildId, userId) {
    const existing = db.prepare(`
        SELECT Id, CAST(GuildId AS TEXT) as GuildId, CAST(UserId AS TEXT) as UserId,
               Score, CorrectAnswers, TotalAnswers, CurrentStreak, BestStreak, LastPlayedAt
        FROM TriviaStats WHERE GuildId = ? AND UserId = ?
    `).get(guildId, userId);

    if (existing) return existing;

    db.prepare(`
        INSERT INTO TriviaStats (GuildId, UserId, Score, CorrectAnswers, TotalAnswers, CurrentStreak, BestStreak, LastPlayedAt)
        VALUES (?, ?, 0, 0, 0, 0, 0, ?)
    `).run(guildId, userId, new Date().toISOString());

    return {
        GuildId: String(guildId),
        UserId: String(userId),
        Score: 0,
        CorrectAnswers: 0,
        TotalAnswers: 0,
        CurrentStreak: 0,
        BestStreak: 0,
        LastPlayedAt: new Date().toISOString()
    };
}

function saveStats(stat) {
    db.prepare(`
        UPDATE TriviaStats 
        SET Score = ?, CorrectAnswers = ?, TotalAnswers = ?, CurrentStreak = ?, BestStreak = ?, LastPlayedAt = ?
        WHERE GuildId = ? AND UserId = ?
    `).run(
        stat.Score,
        stat.CorrectAnswers,
        stat.TotalAnswers,
        stat.CurrentStreak,
        stat.BestStreak,
        new Date().toISOString(),
        stat.GuildId,
        stat.UserId
    );
}

export function getUserTriviaStats(guildId, userId) {
    return db.prepare(`
        SELECT Id, CAST(GuildId AS TEXT) as GuildId, CAST(UserId AS TEXT) as UserId,
               Score, CorrectAnswers, TotalAnswers, CurrentStreak, BestStreak, LastPlayedAt
        FROM TriviaStats WHERE GuildId = ? AND UserId = ?
    `).get(guildId, userId);
}

export function getGuildTriviaLeaderboard(guildId, limit = 10) {
    return db.prepare(`
        SELECT Id, CAST(GuildId AS TEXT) as GuildId, CAST(UserId AS TEXT) as UserId,
               Score, CorrectAnswers, TotalAnswers, CurrentStreak, BestStreak
        FROM TriviaStats
        WHERE GuildId = ? AND TotalAnswers > 0
        ORDER BY Score DESC, CorrectAnswers DESC
        LIMIT ?
    `).all(guildId, limit);
}

export default {
    CATEGORIES,
    decodeHtml,
    extractSmartGifQuery,
    searchGif,
    fetchOpenTdbQuestion,
    isTriviaInteraction,
    handleTriviaButton,
    startTriviaRound,
    getUserTriviaStats,
    getGuildTriviaLeaderboard
};
