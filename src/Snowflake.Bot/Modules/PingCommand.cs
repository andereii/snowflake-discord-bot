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
    [SlashCommand("ping", "Comprueba que el bot está vivo")]
    public async Task PingAsync(InteractionContext ctx)
    {
        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent(msg.Get("Ping:Respuesta", ("latencia", ctx.Client.Ping))));
    }
}
