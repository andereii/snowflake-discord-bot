using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Snowflake.Bot.Services;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Comandos generales del bot.
/// </summary>
public sealed class PingCommand(MessagesService msg) : ApplicationCommandModule
{
    [SlashCommand("ping", "Check that the bot is alive")]
    [NameLocalization(Localization.Spanish, "ping")]
    [NameLocalization(Localization.Portuguese, "ping")]
    [DescriptionLocalization(Localization.Spanish, "Comprueba que el bot está vivo")]
    [DescriptionLocalization(Localization.Portuguese, "Verifica se o bot está vivo")]
    public async Task PingAsync(InteractionContext ctx)
    {
        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent(msg.Get(ctx.Guild.Id, "Ping:Respuesta", ("latencia", ctx.Client.Ping))));
    }
}
