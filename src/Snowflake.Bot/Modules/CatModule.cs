using System.Text;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Snowflake.Bot.Services;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Módulo de fotos aleatorias de gatos tiernos.
/// </summary>
public sealed class CatModule(CatService catService, MessagesService msg) : SnowflakeModuleBase
{
    [SlashCommand("cat", "Get a random cat picture")]
    [NameLocalization(Localization.Spanish, "gato")]
    [NameLocalization(Localization.Portuguese, "gato")]
    [DescriptionLocalization(Localization.Spanish, "Muestra una foto de un gato aleatorio")]
    [DescriptionLocalization(Localization.Portuguese, "Mostra uma foto de um gato aleatório")]
    public async Task CatAsync(InteractionContext ctx)
    {
        await ctx.DeferAsync();

        var fotoUrl = await catService.ObtenerFotoGatoAsync();
        if (string.IsNullOrWhiteSpace(fotoUrl))
        {
            await SafeEditAsync(ctx, msg.Get(ctx.Guild.Id, "Gato:Error"));
            return;
        }

        var titulo = GenerarTituloMew();
        var embed = new DiscordEmbedBuilder()
            .WithTitle(titulo)
            .WithUrl(fotoUrl)
            .WithImageUrl(fotoUrl)
            .WithFooter(fotoUrl)
            .WithColor(new DiscordColor("#f9c2d1"));

        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
    }

    /// <summary>
    /// Genera un título dinámico de 'Mew':
    /// - 'e' variable entre 1 y 20
    /// - 'w' variable entre 1 y 10
    /// - Signos '!' (0 a 10) y '?' (0 a 10)
    /// - ':3' opcional con cantidad de '3' entre 1 y 5
    /// </summary>
    public static string GenerarTituloMew()
    {
        var sb = new StringBuilder();
        sb.Append(Random.Shared.Next(2) == 0 ? "M" : "m");
        sb.Append(new string('e', Random.Shared.Next(1, 21)));
        sb.Append(new string('w', Random.Shared.Next(1, 11)));

        var exclamations = Random.Shared.Next(0, 11);
        var questions = Random.Shared.Next(0, 11);
        if (exclamations > 0) sb.Append(new string('!', exclamations));
        if (questions > 0) sb.Append(new string('?', questions));

        if (Random.Shared.Next(2) == 0)
        {
            var treses = Random.Shared.Next(1, 6);
            sb.Append(" :").Append(new string('3', treses));
        }

        return sb.ToString();
    }
}
