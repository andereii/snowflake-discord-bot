import { SlashCommandBuilder, EmbedBuilder } from 'discord.js';
import MessagesService from '../services/messagesService.js';
import { startTriviaRound, getUserTriviaStats, getGuildTriviaLeaderboard } from '../services/triviaService.js';

export const data = new SlashCommandBuilder()
    .setName('trivia')
    .setDescription('Play trivia games and check rankings')
    .addSubcommand(sub =>
        sub.setName('play')
            .setDescription('Start a new trivia question round')
            .addStringOption(opt =>
                opt.setName('category')
                    .setDescription('Question category (default: Anime & Manga)')
                    .setNameLocalizations({
                        'es-ES': 'categoria',
                        'es-419': 'categoria',
                        'pt-BR': 'categoria'
                    })
                    .setDescriptionLocalizations({
                        'es-ES': 'Categoría de la pregunta (por defecto: Anime y Manga)',
                        'es-419': 'Categoría de la pregunta (por defecto: Anime y Manga)',
                        'pt-BR': 'Categoria da pergunta (padrão: Anime e Mangá)'
                    })
                    .addChoices(
                        { name: 'Anime & Manga (Default)', value: 'anime' },
                        { name: 'General Knowledge', value: 'general' },
                        { name: 'Video Games', value: 'videogames' },
                        { name: 'Film & Cinema', value: 'film' },
                        { name: 'Music', value: 'music' },
                        { name: 'Television', value: 'television' },
                        { name: 'Books & Literature', value: 'books' },
                        { name: 'Comics', value: 'comics' },
                        { name: 'Cartoons & Animation', value: 'cartoons' },
                        { name: 'Science & Nature', value: 'science' },
                        { name: 'Computers & Tech', value: 'computers' },
                        { name: 'Mythology', value: 'mythology' },
                        { name: 'Sports', value: 'sports' },
                        { name: 'Geography', value: 'geography' },
                        { name: 'History', value: 'history' }
                    )
            )
            .addStringOption(opt =>
                opt.setName('difficulty')
                    .setDescription('Question difficulty')
                    .addChoices(
                        { name: 'Easy (+10 pts)', value: 'easy' },
                        { name: 'Medium (+20 pts)', value: 'medium' },
                        { name: 'Hard (+30 pts)', value: 'hard' }
                    )
            )
    )
    .addSubcommand(sub =>
        sub.setName('stats')
            .setDescription('View trivia stats, score and streak')
            .addUserOption(opt =>
                opt.setName('user')
                    .setDescription('User to check (default: yourself)')
            )
    )
    .addSubcommand(sub =>
        sub.setName('leaderboard')
            .setDescription('View top trivia players on this server')
    );

export async function execute(interaction) {
    const subcommand = interaction.options.getSubcommand();
    const guildId = interaction.guildId;

    if (subcommand === 'play') {
        const category = interaction.options.getString('category');
        const difficulty = interaction.options.getString('difficulty');
        await startTriviaRound(interaction, category, difficulty);
        return;
    }

    if (subcommand === 'stats') {
        const target = interaction.options.getUser('user') || interaction.user;
        const stat = getUserTriviaStats(guildId, target.id);

        if (!stat || stat.TotalAnswers === 0) {
            const sinStats = MessagesService.get(guildId, 'Trivia:SinEstadisticas', { usuario: target.username }) || `ℹ️ **${target.username}** no tiene estadísticas de trivia registradas.`;
            return interaction.reply({ content: sinStats, ephemeral: true });
        }

        const precision = stat.TotalAnswers > 0 ? Math.round((stat.CorrectAnswers * 100) / stat.TotalAnswers) : 0;

        const embed = new EmbedBuilder()
            .setTitle(`🏆 ${MessagesService.get(guildId, 'Trivia:TituloStats', { usuario: target.username }) || `Estadísticas de ${target.username}`}`)
            .setThumbnail(target.displayAvatarURL())
            .setColor(0xF1C40F)
            .addFields(
                { name: `⭐ ${MessagesService.get(guildId, 'Trivia:PuntosTotales') || 'Puntos Totales'}`, value: `\`${stat.Score}\` pts`, inline: true },
                { name: `🔥 ${MessagesService.get(guildId, 'Trivia:RachaActual') || 'Racha Actual'}`, value: `\`${stat.CurrentStreak}\` (Mejor: \`${stat.BestStreak}\`)`, inline: true },
                { name: `🎯 ${MessagesService.get(guildId, 'Trivia:Precision') || 'Precisión'}`, value: `\`${precision}%\` (${stat.CorrectAnswers}/${stat.TotalAnswers})`, inline: true }
            )
            .setFooter({ text: `Snowflake Trivia • ${interaction.guild.name}` });

        return interaction.reply({ embeds: [embed] });
    }

    if (subcommand === 'leaderboard') {
        const top = getGuildTriviaLeaderboard(guildId, 10);

        if (!top || top.length === 0) {
            const sinRanking = MessagesService.get(guildId, 'Trivia:SinRanking') || 'ℹ️ Aún no hay registros de trivia en este servidor.';
            return interaction.reply({ content: sinRanking, ephemeral: true });
        }

        const medals = ['🥇', '🥈', '🥉'];
        let desc = '';
        for (let i = 0; i < top.length; i++) {
            const s = top[i];
            const icon = i < 3 ? medals[i] : `**#${i + 1}**`;
            const precision = s.TotalAnswers > 0 ? Math.round((s.CorrectAnswers * 100) / s.TotalAnswers) : 0;
            desc += `${icon} <@${s.UserId}> — **${s.Score} pts** (\`${s.CorrectAnswers}/${s.TotalAnswers}\` aciertos • ${precision}%)\n`;
        }

        const embed = new EmbedBuilder()
            .setTitle(`🏆 ${MessagesService.get(guildId, 'Trivia:TituloRanking') || 'Clasificación de Trivia'}`)
            .setDescription(desc)
            .setColor(0xF1C40F)
            .setFooter({ text: `Snowflake Trivia • ${interaction.guild.name}` });

        return interaction.reply({ embeds: [embed] });
    }
}

export default {
    data,
    execute
};
