using System.Diagnostics;
using System.Globalization;
using DSharpPlus;
using DSharpPlus.Entities;
using Snowflake.Bot.Services.AiCommands;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Services.Calculators;

public sealed record CalculatorResponse(
    bool EsIa,
    DiscordEmbedBuilder? Embed,
    string? TextoIa);

/// <summary>
/// Orquestador del servicio de calculadora y resolución matemática.
/// Realiza evaluación matemática instantánea localmente o delega a DeepSeek IA si es un problema en lenguaje natural.
/// </summary>
public sealed class CalculatorService(
    DiscordClient client,
    DeepSeekService ia,
    MessagesService msg)
{
    public async Task<CalculatorResponse> ProcesarAsync(DiscordGuild guild, DiscordChannel canal, DiscordMember miembro, string entrada)
    {
        var guildId = guild.Id;

        if (string.IsNullOrWhiteSpace(entrada))
        {
            var embedError = new DiscordEmbedBuilder()
                .WithTitle(msg.Get(guildId, "Calculadora:Titulo"))
                .WithDescription(msg.Get(guildId, "Calculadora:ErrorSintaxis", ("error", "Expresión vacía")))
                .WithColor(DiscordColor.Red);
            return new CalculatorResponse(false, embedError, null);
        }

        entrada = entrada.Trim();

        // 1. Si es lenguaje natural explícito, delegar a IA directamente
        if (MathEngine.EsLenguajeNatural(entrada))
        {
            return await ConsultarIaAsync(guild, canal, miembro, entrada).ConfigureAwait(false);
        }

        // 2. Intentar evaluación matemática local
        var sw = Stopwatch.StartNew();
        var res = MathEngine.Evaluar(entrada);
        sw.Stop();

        if (res.Exitoso)
        {
            var embed = ConstruirEmbedResultado(guildId, entrada, res, sw.ElapsedMilliseconds);
            return new CalculatorResponse(false, embed, null);
        }

        // 3. Si falló la evaluación pero contiene incógnitas (ej. "2x + 5 = 15"), intentar con IA
        if (entrada.Any(char.IsLetter) || entrada.Contains('='))
        {
            return await ConsultarIaAsync(guild, canal, miembro, entrada).ConfigureAwait(false);
        }

        // 4. Si es un error aritmético directo (ej. división por cero o sintaxis inválida)
        var errorTexto = msg.Get(guildId, res.ErrorClave ?? "Calculadora:ErrorDesconocido", ("error", res.ErrorDetalle ?? ""));
        var embedFallo = new DiscordEmbedBuilder()
            .WithTitle(msg.Get(guildId, "Calculadora:Titulo"))
            .WithDescription(errorTexto)
            .AddField(msg.Get(guildId, "Calculadora:Expresion"), $"`{entrada}`")
            .WithColor(DiscordColor.Red);

        return new CalculatorResponse(false, embedFallo, null);
    }

    private async Task<CalculatorResponse> ConsultarIaAsync(DiscordGuild guild, DiscordChannel canal, DiscordMember miembro, string entrada)
    {
        var aiCtx = new AiCommandContext(client, guild, canal, miembro);
        var prompt = $"Resuelve de forma clara y paso a paso el siguiente problema matemático o cálculo:\n{entrada}";
        var outcome = await ia.PreguntarAsync(aiCtx, miembro.DisplayName, prompt).ConfigureAwait(false);
        return new CalculatorResponse(true, null, outcome.Texto);
    }

    private DiscordEmbedBuilder ConstruirEmbedResultado(ulong guildId, string entrada, MathResult res, long ms)
    {
        var formattedResult = FormatearNumero(res.Resultado);

        var embed = new DiscordEmbedBuilder()
            .WithTitle(msg.Get(guildId, "Calculadora:Titulo"))
            .WithColor(DiscordColor.Cyan)
            .AddField(msg.Get(guildId, "Calculadora:Expresion"), $"```math\n{entrada}\n```")
            .AddField(msg.Get(guildId, "Calculadora:Resultado"), $"```fix\n{formattedResult}\n```", inline: true);

        if (!string.IsNullOrEmpty(res.FraccionExacta))
        {
            embed.AddField(msg.Get(guildId, "Calculadora:Fraccion"), $"```yaml\n{res.FraccionExacta}\n```", inline: true);
        }

        var tiempoTexto = ms <= 1 ? "<1 ms" : $"{ms} ms";
        embed.WithFooter($"⏱️ {tiempoTexto} • Snowflake Math Engine");

        return embed;
    }

    private static string FormatearNumero(double d)
    {
        if (double.IsInteger(d))
            return d.ToString("N0", CultureInfo.InvariantCulture);

        // Hasta 10 decimales significativos sin ceros a la derecha
        return d.ToString("0.##########", CultureInfo.InvariantCulture);
    }
}
