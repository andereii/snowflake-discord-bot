using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
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
/// traducción automática (Google GTX) y estadísticas por servidor.
/// </summary>
public sealed class TriviaService
{
    private readonly DiscordClient _client;
    private readonly HttpClient _http;
    private readonly TranslationService _translation;
    private readonly GuildSettingsService _settings;
    private readonly IDbContextFactory<BotDbContext> _dbFactory;
    private readonly MessagesService _msg;
    private readonly ILogger<TriviaService> _logger;

    private readonly ConcurrentDictionary<string, TriviaSession> _activeSessions = new();

    public TriviaService(
        DiscordClient client,
        HttpClient http,
        TranslationService translation,
        GuildSettingsService settings,
        IDbContextFactory<BotDbContext> dbFactory,
        MessagesService msg,
        ILogger<TriviaService> logger)
    {
        _client = client;
        _http = http;
        _translation = translation;
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
        var pregunta = await ObtenerPreguntaAsync(ctx.Guild.Id, lang, categoria, dificultad).ConfigureAwait(false);

        if (pregunta is null)
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(_msg.Get(ctx.Guild.Id, "Trivia:ErrorCarga")));
            return;
        }

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
        var pregunta = await ObtenerPreguntaAsync(e.Guild.Id, lang, categoria, dificultad).ConfigureAwait(false);

        if (pregunta is null)
        {
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Trivia:ErrorCarga"));
            return;
        }

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

    // ------------------------- API y Traducción -------------------------

    private async Task<TriviaPregunta?> ObtenerPreguntaAsync(
        ulong guildId,
        string lang,
        string? categoria = null,
        string? dificultad = null)
    {
        try
        {
            var url = "https://opentdb.com/api.php?amount=1&type=multiple";
            if (!string.IsNullOrWhiteSpace(dificultad))
            {
                var difLower = dificultad.ToLowerInvariant();
                if (difLower is "easy" or "medium" or "hard" or "facil" or "medio" or "dificil")
                {
                    var difApi = difLower switch
                    {
                        "facil" => "easy",
                        "medio" => "medium",
                        "dificil" => "hard",
                        _ => difLower
                    };
                    url += $"&difficulty={difApi}";
                }
            }

            if (!string.IsNullOrWhiteSpace(categoria) && MapearCategoria(categoria) is int catId)
            {
                url += $"&category={catId}";
            }

            using var resp = await _http.GetAsync(url).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return FallbackPregunta(lang);

            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            if (root.GetProperty("response_code").GetInt32() != 0)
                return FallbackPregunta(lang);

            var item = root.GetProperty("results")[0];
            var catRaw = WebUtility.HtmlDecode(item.GetProperty("category").GetString() ?? "General Knowledge");
            var difRaw = item.GetProperty("difficulty").GetString() ?? "medium";
            var pregRaw = WebUtility.HtmlDecode(item.GetProperty("question").GetString() ?? "");
            var correctaRaw = WebUtility.HtmlDecode(item.GetProperty("correct_answer").GetString() ?? "");

            var incorrectasRaw = new List<string>();
            foreach (var inc in item.GetProperty("incorrect_answers").EnumerateArray())
                incorrectasRaw.Add(WebUtility.HtmlDecode(inc.GetString() ?? ""));

            // Traducción con Google Translate GTX si no es inglés
            string catTrad = catRaw;
            string difTrad = difRaw;
            string pregTrad = pregRaw;
            string correctaTrad = correctaRaw;
            var incorrectasTrad = new List<string>(incorrectasRaw);

            if (lang is "es" or "pt")
            {
                var textosParaTraducir = new List<string> { catRaw, pregRaw, correctaRaw };
                textosParaTraducir.AddRange(incorrectasRaw);

                var traducidos = await _translation.TraducirLoteAsync(textosParaTraducir, lang, "en").ConfigureAwait(false);
                if (traducidos.Count >= 3 + incorrectasRaw.Count)
                {
                    catTrad = traducidos[0];
                    pregTrad = traducidos[1];
                    correctaTrad = traducidos[2];
                    incorrectasTrad = traducidos.Skip(3).Take(incorrectasRaw.Count).ToList();
                }

                difTrad = difRaw switch
                {
                    "easy" => lang == "es" ? "Fácil" : "Fácil",
                    "medium" => lang == "es" ? "Media" : "Média",
                    "hard" => lang == "es" ? "Difícil" : "Difícil",
                    _ => difRaw
                };
            }
            else
            {
                difTrad = char.ToUpper(difRaw[0]) + difRaw[1..];
            }

            int puntos = difRaw switch
            {
                "easy" => 10,
                "hard" => 30,
                _ => 20
            };

            // Mezclar opciones
            var todasOpciones = new List<string> { correctaTrad };
            todasOpciones.AddRange(incorrectasTrad);
            Mezclar(todasOpciones);

            int indiceCorrecto = todasOpciones.IndexOf(correctaTrad);

            return new TriviaPregunta(catTrad, difTrad, pregTrad, todasOpciones, indiceCorrecto, puntos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obteniendo pregunta de OpenTDB");
            return FallbackPregunta(lang);
        }
    }

    private static int? MapearCategoria(string cat)
    {
        var c = cat.ToLowerInvariant().Trim();
        if (c.Contains("general") || c.Contains("cultura")) return 9;
        if (c.Contains("libro") || c.Contains("book")) return 10;
        if (c.Contains("cine") || c.Contains("pelicula") || c.Contains("film") || c.Contains("movie")) return 11;
        if (c.Contains("musica") || c.Contains("music")) return 12;
        if (c.Contains("videojuego") || c.Contains("game") || c.Contains("gaming")) return 15;
        if (c.Contains("ciencia") || c.Contains("science") || c.Contains("naturaleza")) return 17;
        if (c.Contains("comput") || c.Contains("informatica") || c.Contains("tecnolog")) return 18;
        if (c.Contains("matemat") || c.Contains("math")) return 19;
        if (c.Contains("mitolog") || c.Contains("myth")) return 20;
        if (c.Contains("deporte") || c.Contains("sport") || c.Contains("futbol")) return 21;
        if (c.Contains("geograf") || c.Contains("geo")) return 22;
        if (c.Contains("historia") || c.Contains("history")) return 23;
        if (c.Contains("arte") || c.Contains("art")) return 25;
        if (c.Contains("animal")) return 27;
        if (c.Contains("anime") || c.Contains("manga")) return 31;
        if (c.Contains("caricatura") || c.Contains("cartoon")) return 32;
        return null;
    }

    private static void Mezclar<T>(IList<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = RandomNumberGenerator.GetInt32(n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
    }

    private static TriviaPregunta FallbackPregunta(string lang) => lang switch
    {
        "pt" => new TriviaPregunta(
            "Conhecimento Geral", "Média",
            "Qual é o maior planeta do nosso sistema solar?",
            ["Júpiter", "Saturno", "Terra", "Marte"],
            0, 20),
        "es" => new TriviaPregunta(
            "Cultura General", "Media",
            "¿Cuál es el planeta más grande de nuestro sistema solar?",
            ["Júpiter", "Saturno", "Tierra", "Marte"],
            0, 20),
        _ => new TriviaPregunta(
            "General Knowledge", "Medium",
            "What is the largest planet in our solar system?",
            ["Jupiter", "Saturn", "Earth", "Mars"],
            0, 20)
    };

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
