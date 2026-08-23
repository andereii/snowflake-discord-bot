using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Snowflake.Bot.Services.Calculators;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Módulo de cálculo y matemáticas (/calc).
/// Evalúa expresiones científicas instantáneamente o consulta a la IA para problemas en lenguaje natural.
/// </summary>
public sealed class CalculatorModule : SnowflakeModuleBase
{
    private readonly CalculatorService _calc;

    public CalculatorModule(CalculatorService calc)
    {
        _calc = calc;
    }

    [SlashCommand("calc", "Evaluate math expressions or solve math questions with AI")]
    [NameLocalization(Localization.Spanish, "calc")]
    [NameLocalization(Localization.Portuguese, "calc")]
    [DescriptionLocalization(Localization.Spanish, "Evalúa expresiones matemáticas o resuelve problemas con IA")]
    [DescriptionLocalization(Localization.Portuguese, "Avalia expressões matemáticas ou resolve problemas com IA")]
    public async Task CalcAsync(
        InteractionContext ctx,
        [Option("input", "Math expression (e.g. 5^2, sqrt(25)) or word problem")]
        [NameLocalization(Localization.Spanish, "entrada")]
        [NameLocalization(Localization.Portuguese, "entrada")]
        [DescriptionLocalization(Localization.Spanish, "Expresión matemática (ej. 5^2, sqrt(25)) o problema en texto")]
        [DescriptionLocalization(Localization.Portuguese, "Expressão matemática (ex. 5^2, sqrt(25)) ou problema em texto")] string entrada)
    {
        await ctx.DeferAsync();

        var res = await _calc.ProcesarAsync(ctx.Guild, ctx.Channel, ctx.Member, entrada);

        if (res.EsIa && !string.IsNullOrEmpty(res.TextoIa))
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(res.TextoIa));
        }
        else if (res.Embed is not null)
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(res.Embed));
        }
    }
}
