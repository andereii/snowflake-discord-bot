import { SlashCommandBuilder, PermissionFlagsBits, ChannelType } from 'discord.js';
import db from '../services/database.js';
import MessagesService from '../services/messagesService.js';
import { formatNumber, buildLeaderboard, buildStats, EmojiCorrectDefault, EmojiIncorrectDefault, EmojiRecordDefault } from '../services/countingService.js';

export const data = new SlashCommandBuilder()
    .setName('counting')
    .setDescription('Configure and play the counting game on the server')
    .addSubcommand(sub =>
        sub.setName('channel')
            .setDescription('Set the channel where counting happens')
            .addChannelOption(opt =>
                opt.setName('channel')
                    .setDescription('Text channel for counting')
                    .addChannelTypes(ChannelType.GuildText)
                    .setRequired(true)
            )
    )
    .addSubcommand(sub =>
        sub.setName('disable')
            .setDescription('Unlink the channel and stop reading counting')
    )
    .addSubcommand(sub =>
        sub.setName('base')
            .setDescription('Change the game mode (decimal, binary, octal, hexadecimal)')
            .addStringOption(opt =>
                opt.setName('base')
                    .setDescription('Base to count in')
                    .setRequired(true)
                    .addChoices(
                        { name: 'Decimal', value: 'decimal' },
                        { name: 'Binary', value: 'binario' },
                        { name: 'Octal', value: 'octal' },
                        { name: 'Hexadecimal', value: 'hexadecimal' }
                    )
            )
    )
    .addSubcommand(sub =>
        sub.setName('goal')
            .setDescription('Set a numeric goal for the server')
            .addIntegerOption(opt =>
                opt.setName('number')
                    .setDescription('Goal number to reach')
                    .setRequired(true)
                    .setMinValue(1)
            )
    )
    .addSubcommand(sub =>
        sub.setName('goal-remove')
            .setDescription('Remove the server goal')
    )
    .addSubcommand(sub =>
        sub.setName('icons')
            .setDescription('Choose the emojis the bot reacts with (correct, incorrect, record)')
            .addStringOption(opt =>
                opt.setName('correct')
                    .setDescription('Correct answer emoji (default ✅)')
            )
            .addStringOption(opt =>
                opt.setName('incorrect')
                    .setDescription('Incorrect answer emoji (default ❌)')
            )
            .addStringOption(opt =>
                opt.setName('record')
                    .setDescription('New record emoji (default 🎉)')
            )
    )
    .addSubcommand(sub =>
        sub.setName('lose-message')
            .setDescription('Customize the message when the count is lost (placeholders: {cuenta} {usuario} {siguiente})')
            .addStringOption(opt =>
                opt.setName('message')
                    .setDescription('New message. Empty = reset to default.')
            )
    )
    .addSubcommand(sub =>
        sub.setName('leaderboard')
            .setDescription('Show who has contributed the most to the count')
    )
    .addSubcommand(sub =>
        sub.setName('stats')
            .setDescription("Show a user's stats (or your own)")
            .addUserOption(opt =>
                opt.setName('user')
                    .setDescription('User to look up (empty = yourself)')
            )
    );

function ensureCountingConfig(guildId) {
    let cfg = db.prepare('SELECT * FROM CountingConfigs WHERE GuildId = ?').get(guildId);
    if (!cfg) {
        db.prepare(`
            INSERT INTO CountingConfigs (GuildId, CurrentValue, CurrentRecord, RecordAtChainStart, RecordCelebratedThisChain, Base, ExtraChancesPerDay, ExtraChancesUsedToday)
            VALUES (?, 0, 0, 0, 0, 'Decimal', 0, 0)
        `).run(guildId);
        cfg = db.prepare('SELECT * FROM CountingConfigs WHERE GuildId = ?').get(guildId);
    }
    return cfg;
}

export async function execute(interaction) {
    const subcommand = interaction.options.getSubcommand();
    const guildId = interaction.guildId;
    const member = interaction.member;

    // Subcommands requiring ManageGuild permission
    const adminCommands = ['channel', 'disable', 'base', 'goal', 'goal-remove', 'icons', 'lose-message'];
    if (adminCommands.includes(subcommand)) {
        if (!member.permissions.has(PermissionFlagsBits.ManageGuild)) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'General:SinPermiso') || '❌ No tienes permisos para configurar el conteo (Administrar Servidor requerido).',
                ephemeral: true
            });
        }
    }

    ensureCountingConfig(guildId);

    if (subcommand === 'channel') {
        const canal = interaction.options.getChannel('channel');
        if (canal.type !== ChannelType.GuildText) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'Conteo:CanalDebeSerTexto'),
                ephemeral: true
            });
        }

        db.prepare('UPDATE CountingConfigs SET ChannelId = ? WHERE GuildId = ?').run(canal.id, guildId);
        return interaction.reply({
            content: MessagesService.get(guildId, 'Conteo:CanalEstablecido', { canal: canal.toString() })
        });
    }

    if (subcommand === 'disable') {
        const cfg = db.prepare('SELECT ChannelId FROM CountingConfigs WHERE GuildId = ?').get(guildId);
        if (!cfg || !cfg.ChannelId) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'Conteo:YaDesactivado'),
                ephemeral: true
            });
        }

        db.prepare('UPDATE CountingConfigs SET ChannelId = NULL WHERE GuildId = ?').run(guildId);
        return interaction.reply({
            content: MessagesService.get(guildId, 'Conteo:Desactivado')
        });
    }

    if (subcommand === 'base') {
        const baseVal = interaction.options.getString('base');
        let tipo = 'Decimal';
        if (baseVal === 'binario') tipo = 'Binario';
        else if (baseVal === 'octal') tipo = 'Octal';
        else if (baseVal === 'hexadecimal') tipo = 'Hexadecimal';

        db.prepare('UPDATE CountingConfigs SET Base = ? WHERE GuildId = ?').run(tipo, guildId);
        return interaction.reply({
            content: MessagesService.get(guildId, 'Conteo:BaseEstablecida', { base: tipo })
        });
    }

    if (subcommand === 'goal') {
        const num = interaction.options.getInteger('number');
        if (num <= 0) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'Conteo:ObjetivoInvalido'),
                ephemeral: true
            });
        }

        db.prepare('UPDATE CountingConfigs SET Goal = ? WHERE GuildId = ?').run(num, guildId);
        const cfg = db.prepare('SELECT Base FROM CountingConfigs WHERE GuildId = ?').get(guildId);
        return interaction.reply({
            content: MessagesService.get(guildId, 'Conteo:ObjetivoEstablecido', {
                objetivo: formatNumber(num, cfg?.Base || 'Decimal')
            })
        });
    }

    if (subcommand === 'goal-remove') {
        db.prepare('UPDATE CountingConfigs SET Goal = NULL WHERE GuildId = ?').run(guildId);
        return interaction.reply({
            content: MessagesService.get(guildId, 'Conteo:ObjetivoQuitado')
        });
    }

    if (subcommand === 'icons') {
        const correct = interaction.options.getString('correct');
        const incorrect = interaction.options.getString('incorrect');
        const record = interaction.options.getString('record');

        db.prepare(`
            UPDATE CountingConfigs
            SET EmojiCorrect = COALESCE(?, EmojiCorrect),
                EmojiIncorrect = COALESCE(?, EmojiIncorrect),
                EmojiRecord = COALESCE(?, EmojiRecord)
            WHERE GuildId = ?
        `).run(correct ? correct.trim() : null, incorrect ? incorrect.trim() : null, record ? record.trim() : null, guildId);

        return interaction.reply({
            content: MessagesService.get(guildId, 'Conteo:IconosActualizados', {
                correcto: correct || EmojiCorrectDefault,
                incorrecto: incorrect || EmojiIncorrectDefault,
                record: record || EmojiRecordDefault
            })
        });
    }

    if (subcommand === 'lose-message') {
        const message = interaction.options.getString('message');
        if (!message || !message.trim()) {
            db.prepare('UPDATE CountingConfigs SET LoseMessage = NULL WHERE GuildId = ?').run(guildId);
            return interaction.reply({
                content: MessagesService.get(guildId, 'Conteo:MensajePerdidaBorrado')
            });
        }

        db.prepare('UPDATE CountingConfigs SET LoseMessage = ? WHERE GuildId = ?').run(message.trim(), guildId);
        const cfg = db.prepare('SELECT Base FROM CountingConfigs WHERE GuildId = ?').get(guildId);
        const base = cfg?.Base || 'Decimal';

        const vista = message
            .replace(/{cuenta}/g, formatNumber(42, base))
            .replace(/{usuario}/g, interaction.user.toString())
            .replace(/{siguiente}/g, formatNumber(1, base));

        return interaction.reply({
            content: MessagesService.get(guildId, 'Conteo:MensajePerdidaGuardado', { vista })
        });
    }

    if (subcommand === 'leaderboard') {
        const embed = buildLeaderboard(interaction.guild);
        if (!embed) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'Conteo:LeaderboardVacio'),
                ephemeral: true
            });
        }
        return interaction.reply({ embeds: [embed] });
    }

    if (subcommand === 'stats') {
        const target = interaction.options.getUser('user') || interaction.user;
        const embed = buildStats(interaction.guild, target);
        return interaction.reply({ embeds: [embed] });
    }
}

export default {
    data,
    execute
};
