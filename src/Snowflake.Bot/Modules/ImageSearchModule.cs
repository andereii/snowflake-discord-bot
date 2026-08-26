using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Snowflake.Bot.Services;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Modules;

public sealed class ImageSearchModule(ImageSearchWidgetService _imgWidget, MessagesService _msg) : SnowflakeModuleBase
{
    [SlashCommand("image", "Search for an image")]
    [NameLocalization(Localization.Spanish, "imagen")]
    [NameLocalization(Localization.Portuguese, "imagem")]
    [DescriptionLocalization(Localization.Spanish, "Busca una imagen en la web")]
    [DescriptionLocalization(Localization.Portuguese, "Busca uma imagem na web")]
    public async Task ImageSearchAsync(InteractionContext ctx, 
        [Option("query", "What to search for")]
        [NameLocalization(Localization.Spanish, "consulta")]
        [NameLocalization(Localization.Portuguese, "busca")]
        string query)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
        
        var urls = await _imgWidget.BuscarAsync(query);
        if (urls.Count == 0)
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"{BotEmojis.Error} " + _msg.Get(ctx.Guild.Id, "Herramientas:BusquedaSinResultados", ("consulta", query))));
            return;
        }

        var embed = _imgWidget.ConstruirEmbed(query, urls, 0);
        var botones = _imgWidget.ConstruirBotones();

        var msg = await ctx.EditResponseAsync(new DiscordWebhookBuilder()
            .AddEmbed(embed)
            .AddComponents(botones));
            
        _imgWidget.Registrar(msg.Id, ctx.User.Id, query, urls);
    }
}
