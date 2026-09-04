using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Snowflake.Bot.Services;

namespace Snowflake.Bot.Modules;

public class MagicBallModule : SnowflakeModuleBase
{
    private readonly MessagesService _msg;
    private static readonly Random _random = new();

    private static readonly string[] RespuestasPositivas =
    {
        "Es cierto.",
        "Definitivamente sí.",
        "Sin duda.",
        "Sí, seguro.",
        "Puedes contar con ello.",
        "En mi opinión, sí.",
        "Probablemente.",
        "El universo dice que sí.",
        "Las señales apuntan a que sí.",
        "Todo apunta a que sí."
    };

    private static readonly string[] RespuestasNeutrales =
    {
        "Concéntrate y pregunta de nuevo.",
        "Mejor no te lo digo ahora.",
        "No puedo predecirlo ahora.",
        "Pregunta de nuevo más tarde.",
        "Es complicado, vuelve a intentarlo.",
        "No tengo una respuesta clara para eso.",
        "Los astros no se deciden todavía."
    };

    private static readonly string[] RespuestasNegativas =
    {
        "No cuentes con ello.",
        "Definitivamente no.",
        "Mis fuentes dicen que no.",
        "Muy dudoso.",
        "Lo dudo mucho.",
        "No.",
        "El universo dice que no.",
        "No es buena idea.",
        "Las señales apuntan a que no."
    };

    public MagicBallModule(MessagesService msg)
    {
        _msg = msg;
    }

    [SlashCommand("8ball", "Ask the magic 8-ball a question")]
    [NameLocalization(Localization.Spanish, "bola8")]
    [NameLocalization(Localization.Portuguese, "bola8")]
    [DescriptionLocalization(Localization.Spanish, "Hazle una pregunta a la bola mágica 8")]
    [DescriptionLocalization(Localization.Portuguese, "Faça uma pergunta à bola mágica 8")]
    public async Task EightBallAsync(
        InteractionContext ctx,
        [Option("question", "The question to ask the 8-ball")]
        [NameLocalization(Localization.Spanish, "pregunta")]
        [NameLocalization(Localization.Portuguese, "pergunta")]
        [DescriptionLocalization(Localization.Spanish, "La pregunta que le harás a la bola 8")]
        [DescriptionLocalization(Localization.Portuguese, "A pergunta que você fará à bola 8")]
        string pregunta)
    {
        if (string.IsNullOrWhiteSpace(pregunta) || pregunta.Trim().Length < 3)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Bola8:ErrorPregunta"));
            return;
        }

        string respuesta;
        DiscordColor color;
        int dado = _random.Next(0, 10);
        if (dado < 5)
        {
            respuesta = RespuestasPositivas[_random.Next(RespuestasPositivas.Length)];
            color = DiscordColor.Green;
        }
        else if (dado < 8)
        {
            respuesta = RespuestasNeutrales[_random.Next(RespuestasNeutrales.Length)];
            color = DiscordColor.Yellow;
        }
        else
        {
            respuesta = RespuestasNegativas[_random.Next(RespuestasNegativas.Length)];
            color = DiscordColor.Red;
        }

        var embed = new DiscordEmbedBuilder()
            .WithTitle(_msg.Get(ctx.Guild.Id, "Bola8:Titulo"))
            .WithDescription($"🎱 **{respuesta}**")
            .WithColor(color)
            .AddField(_msg.Get(ctx.Guild.Id, "Bola8:TuPregunta"), $"\"{pregunta}\"")
            .WithFooter(_msg.Get(ctx.Guild.Id, "Bola8:Pie", ("autor", ctx.User.Username)));

        await ResponderAsync(ctx, embed);
    }
}
