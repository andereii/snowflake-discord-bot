using System.Collections.Concurrent;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Snowflake.Bot.Data;
using Snowflake.Bot.Data.Entities;
using Snowflake.Bot.Services.Settings;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Services;

public sealed record TriviaPregunta(
    string Categoria,
    string Dificultad,
    string Pregunta,
    List<string> Opciones,
    int IndiceCorrecto,
    int Puntos);

public sealed class TriviaSession
{
    public required string SessionId { get; init; }
    public required ulong GuildId { get; init; }
    public required ulong ChannelId { get; init; }
    public required ulong UserId { get; init; }
    public required TriviaPregunta Pregunta { get; init; }
    public required DiscordMessage Mensaje { get; set; }
    public TaskCompletionSource<int> RespuestaTcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Servicio central del juego de Trivia interactivo con botones,
/// banco local curado de preguntas i18n (es/en/pt) y estadísticas por servidor.
/// </summary>
public sealed class TriviaService
{
    private readonly DiscordClient _client;
    private readonly GuildSettingsService _settings;
    private readonly IDbContextFactory<BotDbContext> _dbFactory;
    private readonly MessagesService _msg;
    private readonly ILogger<TriviaService> _logger;

    private readonly ConcurrentDictionary<string, TriviaSession> _activeSessions = new();

    public TriviaService(
        DiscordClient client,
        GuildSettingsService settings,
        IDbContextFactory<BotDbContext> dbFactory,
        MessagesService msg,
        ILogger<TriviaService> logger)
    {
        _client = client;
        _settings = settings;
        _dbFactory = dbFactory;
        _msg = msg;
        _logger = logger;

        _client.ComponentInteractionCreated += OnComponentInteractionAsync;
    }

    private async Task OnComponentInteractionAsync(DiscordClient sender, ComponentInteractionCreateEventArgs e)
    {
        if (!e.Id.StartsWith("trivia_ans_")) return;

        var parts = e.Id.Split('_');
        if (parts.Length < 4) return;

        var sessionId = parts[2];
        if (!int.TryParse(parts[3], out var optionIndex)) return;

        if (!_activeSessions.TryGetValue(sessionId, out var session))
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("⚠️ Esta trivia ya ha finalizado.").AsEphemeral(true));
            return;
        }

        if (e.User.Id != session.UserId)
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("❌ Solo el jugador que inició esta trivia puede responder.").AsEphemeral(true));
            return;
        }

        await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
        session.RespuestaTcs.TrySetResult(optionIndex);
    }

    /// <summary>
    /// Inicia una ronda de trivia desde una interacción Slash.
    /// </summary>
    public async Task JugarSlashAsync(
        InteractionContext ctx,
        string? categoria = null,
        string? dificultad = null)
    {
        var lang = (await _settings.GetAsync(ctx.Guild.Id)).Language;
        var pregunta = ObtenerPregunta(lang, categoria, dificultad);

        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var embed = ConstruirEmbedPregunta(ctx.Guild.Id, pregunta, ctx.User);
        var componentes = ConstruirBotones(sessionId, pregunta.Opciones);

        var msgBuilder = new DiscordWebhookBuilder()
            .AddEmbed(embed)
            .AddComponents(componentes.Take(2))
            .AddComponents(componentes.Skip(2).Take(2));

        var msg = await ctx.EditResponseAsync(msgBuilder);

        var session = new TriviaSession
        {
            SessionId = sessionId,
            GuildId = ctx.Guild.Id,
            ChannelId = ctx.Channel.Id,
            UserId = ctx.User.Id,
            Pregunta = pregunta,
            Mensaje = msg
        };

        _activeSessions[sessionId] = session;
        _ = Task.Run(() => ManejarRondaAsync(session, ctx.Member));
    }

    /// <summary>
    /// Inicia una ronda de trivia desde un comando con prefijo ';'.
    /// </summary>
    public async Task JugarPrefixAsync(
        MessageCreateEventArgs e,
        string? categoria = null,
        string? dificultad = null)
    {
        var lang = (await _settings.GetAsync(e.Guild.Id)).Language;
        var pregunta = ObtenerPregunta(lang, categoria, dificultad);

        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var embed = ConstruirEmbedPregunta(e.Guild.Id, pregunta, e.Author);
        var componentes = ConstruirBotones(sessionId, pregunta.Opciones);

        var msgBuilder = new DiscordMessageBuilder()
            .AddEmbed(embed)
            .AddComponents(componentes.Take(2))
            .AddComponents(componentes.Skip(2).Take(2));

        var msg = await e.Channel.SendMessageAsync(msgBuilder);

        var session = new TriviaSession
        {
            SessionId = sessionId,
            GuildId = e.Guild.Id,
            ChannelId = e.Channel.Id,
            UserId = e.Author.Id,
            Pregunta = pregunta,
            Mensaje = msg
        };

        _activeSessions[sessionId] = session;
        var member = e.Message.Author as DiscordMember ?? await e.Guild.GetMemberAsync(e.Author.Id);
        _ = Task.Run(() => ManejarRondaAsync(session, member));
    }

    private async Task ManejarRondaAsync(TriviaSession session, DiscordMember member)
    {
        try
        {
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(25));
            var completedTask = await Task.WhenAny(session.RespuestaTcs.Task, timeoutTask);

            bool respondio = completedTask == session.RespuestaTcs.Task;
            int respuestaIndex = respondio ? await session.RespuestaTcs.Task : -1;

            _activeSessions.TryRemove(session.SessionId, out _);

            bool esCorrecto = respondio && respuestaIndex == session.Pregunta.IndiceCorrecto;
            int puntosGanados = 0;
            TriviaStat? stat = null;

            if (respondio)
            {
                puntosGanados = esCorrecto ? session.Pregunta.Puntos : 0;
                stat = await ActualizarEstadisticasAsync(session.GuildId, session.UserId, esCorrecto, puntosGanados);
            }

            var embedFinal = ConstruirEmbedResultado(session.GuildId, session.Pregunta, member, respondio, esCorrecto, respuestaIndex, puntosGanados, stat);
            var botonesFinales = ConstruirBotonesFinales(session.Pregunta.Opciones, session.Pregunta.IndiceCorrecto, respuestaIndex);

            var editBuilder = new DiscordMessageBuilder()
                .AddEmbed(embedFinal)
                .AddComponents(botonesFinales.Take(2))
                .AddComponents(botonesFinales.Skip(2).Take(2));

            await session.Mensaje.ModifyAsync(editBuilder);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error manejando ronda de trivia {SessionId}", session.SessionId);
        }
    }

    private async Task<TriviaStat> ActualizarEstadisticasAsync(ulong guildId, ulong userId, bool correcto, int puntos)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var stat = await db.TriviaStats.FirstOrDefaultAsync(s => s.GuildId == guildId && s.UserId == userId);
        if (stat is null)
        {
            stat = new TriviaStat
            {
                GuildId = guildId,
                UserId = userId
            };
            db.TriviaStats.Add(stat);
        }

        stat.TotalAnswers++;
        stat.LastPlayedAt = DateTimeOffset.UtcNow;

        if (correcto)
        {
            stat.CorrectAnswers++;
            stat.Score += puntos;
            stat.CurrentStreak++;
            if (stat.CurrentStreak > stat.BestStreak)
                stat.BestStreak = stat.CurrentStreak;
        }
        else
        {
            stat.CurrentStreak = 0;
        }

        await db.SaveChangesAsync();
        return stat;
    }

    public async Task<TriviaStat?> ObtenerEstadisticasAsync(ulong guildId, ulong userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.TriviaStats.AsNoTracking().FirstOrDefaultAsync(s => s.GuildId == guildId && s.UserId == userId);
    }

    public async Task<List<TriviaStat>> ObtenerLeaderboardAsync(ulong guildId, int top = 10)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.TriviaStats.AsNoTracking()
            .Where(s => s.GuildId == guildId)
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => s.CorrectAnswers)
            .Take(top)
            .ToListAsync();
    }

    private static TriviaPregunta ObtenerPregunta(string lang, string? categoria = null, string? dificultad = null)
    {
        return TriviaBank.ObtenerPreguntaAleatoria(lang, categoria, dificultad);
    }

    // ------------------------- Embeds y Botones -------------------------

    private DiscordEmbedBuilder ConstruirEmbedPregunta(ulong guildId, TriviaPregunta p, DiscordUser user)
    {
        return new DiscordEmbedBuilder()
            .WithTitle($"❓ {_msg.Get(guildId, "Trivia:Titulo")}")
            .WithColor(DiscordColor.CornflowerBlue)
            .AddField($"🏷️ {_msg.Get(guildId, "Trivia:Categoria")}", p.Categoria, inline: true)
            .AddField($"⚡ {_msg.Get(guildId, "Trivia:Dificultad")}", p.Dificultad, inline: true)
            .AddField($"🏆 {_msg.Get(guildId, "Trivia:Recompensa")}", $"+{p.Puntos} pts", inline: true)
            .AddField($"📝 {_msg.Get(guildId, "Trivia:Pregunta")}", $"**{p.Pregunta}**")
            .WithFooter($"⏱️ {_msg.Get(guildId, "Trivia:TiempoLimite")} • Jugador: {user.Username}", user.AvatarUrl);
    }

    private DiscordEmbedBuilder ConstruirEmbedResultado(
        ulong guildId,
        TriviaPregunta p,
        DiscordMember member,
        bool respondio,
        bool esCorrecto,
        int seleccion,
        int puntos,
        TriviaStat? stat)
    {
        var embed = new DiscordEmbedBuilder();
        var correctaTexto = p.Opciones[p.IndiceCorrecto];

        if (!respondio)
        {
            embed.WithTitle($"⏰ {_msg.Get(guildId, "Trivia:TiempoAgotado")}")
                 .WithColor(DiscordColor.DarkGray)
                 .WithDescription(_msg.Get(guildId, "Trivia:TiempoAgotadoDesc", ("correcta", correctaTexto)));
        }
        else if (esCorrecto)
        {
            embed.WithTitle($"🎉 {_msg.Get(guildId, "Trivia:RespuestaCorrecta")}")
                 .WithColor(DiscordColor.SpringGreen)
                 .WithDescription(_msg.Get(guildId, "Trivia:GanastePuntos", ("puntos", puntos.ToString()), ("usuario", member.DisplayName)));

            if (stat is not null)
            {
                embed.AddField("📊 " + _msg.Get(guildId, "Trivia:TusEstadisticas"),
                    $"⭐ **{_msg.Get(guildId, "Trivia:PuntosTotales")}:** `{stat.Score}`\n🔥 **{_msg.Get(guildId, "Trivia:RachaActual")}:** `{stat.CurrentStreak}` (Mejor: `{stat.BestStreak}`)\n🎯 **{_msg.Get(guildId, "Trivia:Precision")}:** `{(stat.TotalAnswers > 0 ? (stat.CorrectAnswers * 100 / stat.TotalAnswers) : 0)}%` ({stat.CorrectAnswers}/{stat.TotalAnswers})");
            }
        }
        else
        {
            var elegidaTexto = (seleccion >= 0 && seleccion < p.Opciones.Count) ? p.Opciones[seleccion] : "—";
            embed.WithTitle($"❌ {_msg.Get(guildId, "Trivia:RespuestaIncorrecta")}")
                 .WithColor(DiscordColor.Red)
                 .WithDescription(_msg.Get(guildId, "Trivia:FallasteDesc", ("elegida", elegidaTexto), ("correcta", correctaTexto)));

            if (stat is not null)
            {
                embed.AddField("📊 " + _msg.Get(guildId, "Trivia:TusEstadisticas"),
                    $"⭐ **{_msg.Get(guildId, "Trivia:PuntosTotales")}:** `{stat.Score}`\n🔥 **{_msg.Get(guildId, "Trivia:RachaActual")}:** `0` (Mejor: `{stat.BestStreak}`)");
            }
        }

        embed.AddField($"📝 {_msg.Get(guildId, "Trivia:Pregunta")}", $"*{p.Pregunta}*");
        embed.WithFooter($"Snowflake Trivia • {member.DisplayName}", member.AvatarUrl);
        return embed;
    }

    private static List<DiscordButtonComponent> ConstruirBotones(string sessionId, List<string> opciones)
    {
        var letras = new[] { "A", "B", "C", "D" };
        var botones = new List<DiscordButtonComponent>();

        for (int i = 0; i < opciones.Count && i < 4; i++)
        {
            var texto = TruncarTexto($"{letras[i]}) {opciones[i]}", 80);
            botones.Add(new DiscordButtonComponent(
                ButtonStyle.Primary,
                $"trivia_ans_{sessionId}_{i}",
                texto));
        }

        return botones;
    }

    private static List<DiscordButtonComponent> ConstruirBotonesFinales(
        List<string> opciones,
        int indiceCorrecto,
        int indiceSeleccionado)
    {
        var letras = new[] { "A", "B", "C", "D" };
        var botones = new List<DiscordButtonComponent>();

        for (int i = 0; i < opciones.Count && i < 4; i++)
        {
            var estilo = ButtonStyle.Secondary;

            if (i == indiceCorrecto)
            {
                estilo = ButtonStyle.Success; // Verde para la respuesta correcta
            }
            else if (i == indiceSeleccionado)
            {
                estilo = ButtonStyle.Danger; // Rojo si el usuario eligió esta incorrecta
            }

            var texto = TruncarTexto($"{letras[i]}) {opciones[i]}", 80);
            botones.Add(new DiscordButtonComponent(
                estilo,
                $"trivia_done_{i}",
                texto,
                disabled: true));
        }

        return botones;
    }

    private static string TruncarTexto(string s, int max)
    {
        if (s.Length <= max) return s;
        return s[..(max - 1)] + "…";
    }
}
