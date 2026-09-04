import { EmbedBuilder } from 'discord.js';
import db from './database.js';
import MessagesService from './messagesService.js';
import { evaluate } from 'mathjs';

export const CountingBase = {
    Decimal: 'Decimal',
    Binario: 'Binario',
    Octal: 'Octal',
    Hexadecimal: 'Hexadecimal'
};

export const EmojiCorrectDefault = '✅';
export const EmojiIncorrectDefault = '❌';
export const EmojiRecordDefault = '🎉';
export const EmojiPardonDefault = '🛡️';

// In-memory state for tracking collisions, implicit responses, and single warnings
const duplicateWarned = new Map();     // Map<guildId, boolean>
const lastCountWasImplicit = new Map(); // Map<guildId, boolean>
const implicitWarned = new Map();      // Map<guildId, boolean>

export function formatNumber(val, base = 'Decimal') {
    const v = BigInt(val);
    const b = (base || 'Decimal').toLowerCase();
    if (b === 'binario' || b === 'binary') return v.toString(2);
    if (b === 'octal') return v.toString(8);
    if (b === 'hexadecimal' || b === 'hex') return v.toString(16).toUpperCase();
    return v.toString(10);
}

export function parseNumber(text, base = 'Decimal') {
    if (!text || typeof text !== 'string') return null;
    const clean = text.trim();
    if (!clean) return null;

    const b = (base || 'Decimal').toLowerCase();

    try {
        if (b === 'decimal') {
            // Check direct integer parse first
            if (/^\d+$/.test(clean)) {
                const num = BigInt(clean);
                return num > 0n ? num : null;
            }

            // Math expression evaluation
            try {
                const evaluated = evaluate(clean);
                if (typeof evaluated === 'number' && !isNaN(evaluated) && isFinite(evaluated)) {
                    const rounded = BigInt(Math.round(evaluated));
                    return rounded > 0n ? rounded : null;
                }
            } catch {}
            return null;
        }

        if (b === 'binario' || b === 'binary') {
            if (!/^[01]+$/.test(clean)) return null;
            const parsed = BigInt('0b' + clean);
            return parsed > 0n ? parsed : null;
        }

        if (b === 'octal') {
            if (!/^[0-7]+$/.test(clean)) return null;
            const parsed = BigInt('0o' + clean);
            return parsed > 0n ? parsed : null;
        }

        if (b === 'hexadecimal' || b === 'hex') {
            if (!/^[0-9a-fA-F]+$/.test(clean)) return null;
            const parsed = BigInt('0x' + clean);
            return parsed > 0n ? parsed : null;
        }

        return null;
    } catch {
        return null;
    }
}

/**
 * Process a message in the counting channel
 */
export async function processCountingMessage(message) {
    try {
        if (message.author.bot || !message.guild) return;

        const guildId = message.guild.id;
        const cfg = db.prepare(`
            SELECT CAST(GuildId AS TEXT) as GuildId, CAST(ChannelId AS TEXT) as ChannelId,
                   CurrentValue, CAST(LastUserId AS TEXT) as LastUserId,
                   CurrentRecord, RecordAtChainStart, RecordCelebratedThisChain,
                   Base, Goal, EmojiCorrect, EmojiIncorrect, EmojiRecord, LoseMessage
            FROM CountingConfigs WHERE GuildId = ?
        `).get(guildId);

        if (!cfg || !cfg.ChannelId || String(cfg.ChannelId) !== message.channel.id) {
            return;
        }

        const base = cfg.Base || 'Decimal';
        const parsedValue = parseNumber(message.content, base);
        if (parsedValue === null) return; // Not a number, ignore

        // Get or create user counting stat
        let stat = db.prepare(`
            SELECT Id, CAST(GuildId AS TEXT) as GuildId, CAST(UserId AS TEXT) as UserId,
                   TotalCounts, IncorrectCounts, BestContribution, UserChances, RegenProgress
            FROM CountingStats WHERE GuildId = ? AND UserId = ?
        `).get(guildId, message.author.id);

        if (!stat) {
            db.prepare(`
                INSERT INTO CountingStats (GuildId, UserId, TotalCounts, IncorrectCounts, BestContribution, UserChances, RegenProgress)
                VALUES (?, ?, 0, 0, 0, 2, 0)
            `).run(guildId, message.author.id);
            stat = {
                GuildId: guildId,
                UserId: message.author.id,
                TotalCounts: 0,
                IncorrectCounts: 0,
                BestContribution: 0,
                UserChances: 2,
                RegenProgress: 0
            };
        }

        const expectedValue = BigInt(cfg.CurrentValue || 0) + 1n;
        const isSameUser = Number(cfg.CurrentValue || 0) > 0 && cfg.LastUserId && String(cfg.LastUserId) === message.author.id;

        if (parsedValue === expectedValue && !isSameUser) {
            // ==========================================
            // CASE 1: Correct count!
            // ==========================================
            const isNewRecord = Number(parsedValue) > (cfg.RecordAtChainStart || 0) && (cfg.RecordAtChainStart || 0) > 0;
            const celebrateRecord = isNewRecord && !cfg.RecordCelebratedThisChain;

            const newRecord = Math.max(Number(cfg.CurrentRecord || 0), Number(parsedValue));
            const newBestContrib = Math.max(Number(stat.BestContribution || 0), Number(parsedValue));

            // Regenerate personal saves: 50 correct answers = +1 save (max 2)
            let newChances = stat.UserChances ?? 2;
            let newRegen = (stat.RegenProgress ?? 0);

            if (newChances < 2) {
                newRegen++;
                if (newRegen >= 50) {
                    newChances++;
                    newRegen = 0;
                }
            }

            db.prepare(`
                UPDATE CountingConfigs
                SET CurrentValue = ?, LastUserId = ?, CurrentRecord = ?,
                    RecordCelebratedThisChain = ?
                WHERE GuildId = ?
            `).run(
                Number(parsedValue),
                message.author.id,
                newRecord,
                celebrateRecord ? 1 : (cfg.RecordCelebratedThisChain || 0),
                guildId
            );

            db.prepare(`
                UPDATE CountingStats
                SET TotalCounts = TotalCounts + 1, BestContribution = ?,
                    UserChances = ?, RegenProgress = ?
                WHERE GuildId = ? AND UserId = ?
            `).run(newBestContrib, newChances, newRegen, guildId, message.author.id);

            // Clear temporary warnings & set implicit flag for next turn
            duplicateWarned.set(guildId, false);
            implicitWarned.set(guildId, false);

            const isImplicit = message.content.trim() !== parsedValue.toString();
            lastCountWasImplicit.set(guildId, isImplicit);

            const emoji = isNewRecord
                ? (cfg.EmojiRecord || EmojiRecordDefault)
                : (cfg.EmojiCorrect || EmojiCorrectDefault);

            await message.react(emoji).catch(() => {});

            if (cfg.Goal && Number(parsedValue) === Number(cfg.Goal)) {
                const goalMsg = MessagesService.get(guildId, 'Conteo:ObjetivoAlcanzado', {
                    objetivo: formatNumber(cfg.Goal, base)
                });
                await message.channel.send(goalMsg).catch(() => {});
            }
        } else {
            // ==========================================
            // CASE 2: Incorrect count / Mistake
            // ==========================================
            const hasRestrictedUser = Number(cfg.CurrentValue || 0) > 0 && !!cfg.LastUserId;
            const ultimoUsuarioMention = hasRestrictedUser ? `<@${cfg.LastUserId}>` : '';
            const nextFormatted = formatNumber(expectedValue, base);

            // Subcase 2A: Colisión (repetición del número anterior inmediato)
            const isDuplicatePrevious = parsedValue === BigInt(cfg.CurrentValue || 0);
            if (isDuplicatePrevious && !duplicateWarned.get(guildId)) {
                duplicateWarned.set(guildId, true);
                const colisionKey = hasRestrictedUser ? 'Conteo:AvisoColision' : 'Conteo:AvisoColisionLibre';
                const colisionMsg = MessagesService.get(guildId, colisionKey, {
                    usuario: message.author.toString(),
                    actual: formatNumber(cfg.CurrentValue || 0, base),
                    siguiente: nextFormatted,
                    ultimoUsuario: ultimoUsuarioMention
                });
                await message.react('⚠️').catch(() => {});
                const colisionEmbed = new EmbedBuilder()
                    .setTitle(MessagesService.get(guildId, 'Conteo:AvisoColisionTitulo') || '⚠️ Número duplicado')
                    .setDescription(colisionMsg)
                    .setColor(0xF1C40F);
                await message.channel.send({ embeds: [colisionEmbed] }).catch(() => {});
                return;
            }

            // Subcase 2B: Confusión por respuesta implícita matemática previa (dentro del rango ±5)
            const wasImplicit = lastCountWasImplicit.get(guildId) === true;
            const diff = Math.abs(Number(parsedValue) - Number(expectedValue));
            if (wasImplicit && diff <= 5 && !implicitWarned.get(guildId)) {
                implicitWarned.set(guildId, true);
                const implicitKey = hasRestrictedUser ? 'Conteo:AvisoImplicito' : 'Conteo:AvisoImplicitoLibre';
                const implicitMsg = MessagesService.get(guildId, implicitKey, {
                    usuario: message.author.toString(),
                    siguiente: nextFormatted,
                    ultimoUsuario: ultimoUsuarioMention
                });
                await message.react('⚠️').catch(() => {});
                const implicitEmbed = new EmbedBuilder()
                    .setTitle(MessagesService.get(guildId, 'Conteo:AvisoImplicitoTitulo') || '⚠️ Respuesta implícita previa')
                    .setDescription(implicitMsg)
                    .setColor(0xF1C40F);
                await message.channel.send({ embeds: [implicitEmbed] }).catch(() => {});
                return;
            }

            // Subcase 2C: Error normal o advertencia previa ya consumida
            // Verificar si el usuario tiene protectores individuales disponibles
            const currentChances = stat.UserChances ?? 2;

            if (currentChances > 0) {
                const remainingChances = currentChances - 1;
                db.prepare(`
                    UPDATE CountingStats
                    SET UserChances = ?, IncorrectCounts = IncorrectCounts + 1
                    WHERE GuildId = ? AND UserId = ?
                `).run(remainingChances, guildId, message.author.id);

                await message.react(EmojiPardonDefault).catch(() => {});

                const saveKey = hasRestrictedUser ? 'Conteo:ProtectorUsado' : 'Conteo:ProtectorUsadoLibre';
                const saveUsedMsg = MessagesService.get(guildId, saveKey, {
                    usuario: message.author.toString(),
                    restantes: remainingChances,
                    siguiente: nextFormatted,
                    ultimoUsuario: ultimoUsuarioMention
                });

                const saveEmbed = new EmbedBuilder()
                    .setTitle(MessagesService.get(guildId, 'Conteo:ProtectorUsadoTitulo') || '🛡️ Protector usado')
                    .setDescription(saveUsedMsg)
                    .setColor(0x3498DB);

                await message.channel.send({ embeds: [saveEmbed] }).catch(() => {});
                return; // Racha protegida
            }

            // Subcase 2D: Sin protectores -> Se rompe la racha
            db.prepare('UPDATE CountingStats SET IncorrectCounts = IncorrectCounts + 1 WHERE GuildId = ? AND UserId = ?')
                .run(guildId, message.author.id);

            await message.react(cfg.EmojiIncorrect || EmojiIncorrectDefault).catch(() => {});

            const countFormatted = formatNumber(cfg.CurrentValue || 0, base);
            const resetNextFormatted = formatNumber(1, base);

            let loseMsg;
            if (isSameUser) {
                loseMsg = MessagesService.get(guildId, 'Conteo:MismoUsuario', {
                    usuario: message.author.toString(),
                    siguiente: resetNextFormatted
                });
            } else if (cfg.LoseMessage) {
                loseMsg = cfg.LoseMessage
                    .replace(/{cuenta}/g, countFormatted)
                    .replace(/{usuario}/g, message.author.toString())
                    .replace(/{siguiente}/g, resetNextFormatted);
            } else {
                loseMsg = MessagesService.get(guildId, 'Conteo:Perdiste', {
                    cuenta: countFormatted,
                    usuario: message.author.toString(),
                    siguiente: resetNextFormatted
                });
            }

            if (cfg.RecordCelebratedThisChain) {
                loseMsg += ` (Nuevo récord: ${formatNumber(cfg.CurrentRecord || 0, base)})`;
            }

            await message.channel.send(loseMsg).catch(() => {});

            // Reset chain
            db.prepare(`
                UPDATE CountingConfigs
                SET CurrentValue = 0, LastUserId = NULL,
                    RecordAtChainStart = CurrentRecord, RecordCelebratedThisChain = 0
                WHERE GuildId = ?
            `).run(guildId);

            duplicateWarned.set(guildId, false);
            implicitWarned.set(guildId, false);
            lastCountWasImplicit.set(guildId, false);
        }
    } catch (err) {
        console.error('[countingService] Error processing counting message:', err);
    }
}

export function buildLeaderboard(guild) {
    const guildId = guild.id;
    const rows = db.prepare(`
        SELECT CAST(UserId AS TEXT) as UserId, TotalCounts, BestContribution
        FROM CountingStats
        WHERE GuildId = ? AND TotalCounts > 0
        ORDER BY TotalCounts DESC
        LIMIT 10
    `).all(guildId);

    if (!rows || rows.length === 0) return null;

    const medals = ['🥇', '🥈', '🥉'];
    let desc = '';
    for (let i = 0; i < rows.length; i++) {
        const medal = i < 3 ? medals[i] : `\`#${i + 1}\``;
        desc += `${medal} <@${rows[i].UserId}> — **${rows[i].TotalCounts.toLocaleString()}**\n`;
    }

    return new EmbedBuilder()
        .setTitle(MessagesService.get(guildId, 'Conteo:LeaderboardTitulo'))
        .setDescription(desc)
        .setColor(0x5865F2);
}

export function buildStats(guild, targetUser) {
    const guildId = guild.id;
    const userId = targetUser.id;

    const cfg = db.prepare('SELECT Base FROM CountingConfigs WHERE GuildId = ?').get(guildId);
    const base = cfg?.Base || 'Decimal';

    const stat = db.prepare(`
        SELECT TotalCounts, IncorrectCounts, BestContribution, UserChances, RegenProgress
        FROM CountingStats WHERE GuildId = ? AND UserId = ?
    `).get(guildId, userId);

    const title = MessagesService.get(guildId, 'Conteo:StatsTitulo', { usuario: targetUser.username });

    if (!stat || (stat.TotalCounts === 0 && stat.IncorrectCounts === 0)) {
        return new EmbedBuilder()
            .setTitle(title)
            .setDescription(MessagesService.get(guildId, 'Conteo:StatsSinDatos'))
            .setColor(0x5865F2);
    }

    const total = stat.TotalCounts + stat.IncorrectCounts;
    const precision = total === 0 ? 100 : ((stat.TotalCounts * 100) / total).toFixed(1);
    const chances = stat.UserChances ?? 2;
    const regen = chances >= 2 ? '2/2 (Max)' : `${stat.RegenProgress ?? 0}/50`;

    return new EmbedBuilder()
        .setTitle(title)
        .setColor(0x5865F2)
        .addFields(
            { name: MessagesService.get(guildId, 'Conteo:StatsTotal'), value: stat.TotalCounts.toLocaleString(), inline: true },
            { name: MessagesService.get(guildId, 'Conteo:StatsIncorrectos'), value: stat.IncorrectCounts.toLocaleString(), inline: true },
            { name: MessagesService.get(guildId, 'Conteo:StatsPrecision'), value: `${precision}%`, inline: true },
            { name: MessagesService.get(guildId, 'Conteo:StatsMejor'), value: formatNumber(stat.BestContribution, base), inline: true },
            { name: MessagesService.get(guildId, 'Conteo:StatsProtectores') || 'Protectores', value: `\`${chances}/2\``, inline: true },
            { name: MessagesService.get(guildId, 'Conteo:StatsRegen') || 'Recarga de protector', value: `\`${regen}\``, inline: true }
        );
}

export default {
    formatNumber,
    parseNumber,
    processCountingMessage,
    buildLeaderboard,
    buildStats
};
