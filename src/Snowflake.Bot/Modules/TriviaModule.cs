using System.Text;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Snowflake.Bot.Services;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Módulo de Trivia y Cultura General (/trivia).
/// Preguntas interactivas con botones traducidas automáticamente al idioma del servidor.
/// </summary>
[SlashCommandGroup("trivia", "Play trivia games and check rankings")]
[DescriptionLocalization(Localization.Spanish, "Juega a la trivia cultural y consulta las clasificaciones")]
[DescriptionLocalization(Localization.Portuguese, "Jogue jogos de curiosidades e veja o ranking")]
public sealed class TriviaModule : SnowflakeModuleBase
{
    private readonly TriviaService _trivia;
    private readonly MessagesService _msg;

    public TriviaModule(TriviaService trivia, MessagesService msg)
    {
        _trivia = trivia;
        _msg = msg;
    }

    [SlashCommand("play", "Start a new trivia question round")]
    [NameLocalization(Localization.Spanish, "jugar")]
    [NameLocalization(Localization.Portuguese, "jogar")]
    [DescriptionLocalization(Localization.Spanish, "Inicia una ronda de preguntas de trivia cultural")]
    [DescriptionLocalization(Localization.Portuguese, "Inicia uma rodada de perguntas de curiosidades")]
    public async Task JugarAsync(
        InteractionContext ctx,
        [Option("category", "Question category")]
        [NameLocalization(Localization.Spanish, "categoria")]
        [NameLocalization(Localization.Portuguese, "categoria")]
        [DescriptionLocalization(Localization.Spanish, "Categoría de la pregunta")]
        [DescriptionLocalization(Localization.Portuguese, "Categoria da pergunta")]
        [Choice("General Knowledge", "general"),
         Choice("Science & Tech", "ciencia"),
         Choice("History", "historia"),
         Choice("Geography", "geografia"),
         Choice("Video Games", "videojuegos"),
         Choice("Anime & Manga", "anime"),
         Choice("Films & Cinema", "cine"),
         Choice("Music", "musica"),
         Choice("Mythology", "mitologia"),
         Choice("Sports", "deportes")] string? categoria = null,
        [Option("difficulty", "Question difficulty")]
        [NameLocalization(Localization.Spanish, "dificultad")]
        [NameLocalization(Localization.Portuguese, "dificuldade")]
        [DescriptionLocalization(Localization.Spanish, "Dificultad de la pregunta")]
        [DescriptionLocalization(Localization.Portuguese, "Dificuldade da pergunta")]
        [Choice("Easy (+10 pts)", "easy"),
         Choice("Medium (+20 pts)", "medium"),
         Choice("Hard (+30 pts)", "hard")] string? dificultad = null)
    {
        await ctx.DeferAsync();
        await _trivia.JugarSlashAsync(ctx, categoria, dificultad);
    }

    [SlashCommand("stats", "View trivia stats, score and streak")]
    [NameLocalization(Localization.Spanish, "estadisticas")]
    [NameLocalization(Localization.Portuguese, "estatisticas")]
    [DescriptionLocalization(Localization.Spanish, "Consulta tus estadísticas de trivia, puntos y racha")]
    [DescriptionLocalization(Localization.Portuguese, "Veja suas estatísticas de curiosidades, pontos e sequência")]
    public async Task StatsAsync(
        InteractionContext ctx,
        [Option("user", "User to check (default: yourself)")]
        [NameLocalization(Localization.Spanish, "usuario")]
        [NameLocalization(Localization.Portuguese, "usuario")]
        [DescriptionLocalization(Localization.Spanish, "Usuario a consultar (por defecto: tú)")]
        [DescriptionLocalization(Localization.Portuguese, "Usuário a consultar (padrão: você)")] DiscordUser? usuario = null)
    {
        var target = usuario ?? ctx.User;
        var member = target as DiscordMember ?? await ctx.Guild.GetMemberAsync(target.Id);
        var stat = await _trivia.ObtenerEstadisticasAsync(ctx.Guild.Id, target.Id);

        if (stat is null || stat.TotalAnswers == 0)
        {
            var sinStats = _msg.Get(ctx.Guild.Id, "Trivia:SinEstadisticas", ("usuario", member.DisplayName));
            await ResponderAsync(ctx, sinStats, ephemeral: true);
            return;
        }

        var precision = stat.TotalAnswers > 0 ? (stat.CorrectAnswers * 100 / stat.TotalAnswers) : 0;

        var embed = new DiscordEmbedBuilder()
            .WithTitle($"🏆 {_msg.Get(ctx.Guild.Id, "Trivia:TituloStats", ("usuario", member.DisplayName))}")
            .WithThumbnail(member.AvatarUrl)
            .WithColor(DiscordColor.Gold)
            .AddField($"⭐ {_msg.Get(ctx.Guild.Id, "Trivia:PuntosTotales")}", $"`{stat.Score}` pts", inline: true)
            .AddField($"🔥 {_msg.Get(ctx.Guild.Id, "Trivia:RachaActual")}", $"`{stat.CurrentStreak}` (Mejor: `{stat.BestStreak}`)", inline: true)
            .AddField($"🎯 {_msg.Get(ctx.Guild.Id, "Trivia:Precision")}", $"`{precision}%` ({stat.CorrectAnswers}/{stat.TotalAnswers})", inline: true)
            .WithFooter($"Snowflake Trivia • {ctx.Guild.Name}");

        await ResponderAsync(ctx, embed);
    }

    [SlashCommand("leaderboard", "View top trivia players on this server")]
    [NameLocalization(Localization.Spanish, "ranking")]
    [NameLocalization(Localization.Portuguese, "ranking")]
    [DescriptionLocalization(Localization.Spanish, "Consulta el top de mejores jugadores de trivia del servidor")]
    [DescriptionLocalization(Localization.Portuguese, "Veja o top dos melhores jogadores de curiosidades do servidor")]
    public async Task LeaderboardAsync(InteractionContext ctx)
    {
        var top = await _trivia.ObtenerLeaderboardAsync(ctx.Guild.Id, 10);

        if (top.Count == 0)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Trivia:SinRanking"), ephemeral: true);
            return;
        }

        var sb = new StringBuilder();
        var medallas = new[] { "🥇", "🥈", "🥉" };

        for (int i = 0; i < top.Count; i++)
        {
            var s = top[i];
            var icono = i < 3 ? medallas[i] : $"**#{i + 1}**";
            var precision = s.TotalAnswers > 0 ? (s.CorrectAnswers * 100 / s.TotalAnswers) : 0;
            sb.AppendLine($"{icono} <@{s.UserId}> — **{s.Score} pts** (`{s.CorrectAnswers}/{s.TotalAnswers}` aciertos • {precision}%)");
        }

        var embed = new DiscordEmbedBuilder()
            .WithTitle($"🏆 {_msg.Get(ctx.Guild.Id, "Trivia:TituloRanking")}")
            .WithDescription(sb.ToString())
            .WithColor(DiscordColor.Gold)
            .WithFooter($"Snowflake Trivia • {ctx.Guild.Name}");

        await ResponderAsync(ctx, embed);
    }
}
