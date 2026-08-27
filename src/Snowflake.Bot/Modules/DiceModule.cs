using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Snowflake.Bot.Services;

namespace Snowflake.Bot.Modules;

public class DiceModule : SnowflakeModuleBase
{
    private readonly MessagesService _msg;
    private static readonly Random _random = new();

    public DiceModule(MessagesService msg)
    {
        _msg = msg;
    }

    [SlashCommand("roll", "Roll a dice (default 6 faces, max 100)")]
    [NameLocalization(Localization.Spanish, "tirar")]
    [NameLocalization(Localization.Portuguese, "rolar")]
    [DescriptionLocalization(Localization.Spanish, "Lanza un dado (6 caras por defecto, máximo 100)")]
    [DescriptionLocalization(Localization.Portuguese, "Lança um dado (6 faces por padrão, máximo 100)")]
    public async Task RollAsync(
        InteractionContext ctx,
        [Option("faces", "Number of faces (2-100, default 6)")]
        [NameLocalization(Localization.Spanish, "caras")]
        [NameLocalization(Localization.Portuguese, "faces")]
        [DescriptionLocalization(Localization.Spanish, "Número de caras (2-100, 6 por defecto)")]
        [DescriptionLocalization(Localization.Portuguese, "Número de faces (2-100, 6 por padrão)")]
        long faces = 6)
    {
        if (faces < 2 || faces > 100)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Dados:RangoInvalido"));
            return;
        }

        var resultado = _random.NextInt64(1, faces + 1);
        var embed = new DiscordEmbedBuilder()
            .WithTitle(_msg.Get(ctx.Guild.Id, "Dados:Titulo"))
            .WithDescription(_msg.Get(ctx.Guild.Id, "Dados:Resultado", ("dado", resultado), ("caras", faces)))
            .WithColor(DiscordColor.Purple)
            .WithFooter(_msg.Get(ctx.Guild.Id, "Dados:Pie", ("autor", ctx.User.Username)));

        await ResponderAsync(ctx, embed);
    }
}
