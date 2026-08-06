using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Clase base de todos los módulos de comandos. Centraliza los patrones de
/// respuesta (texto, embed, error efímero, edición de respuestas diferidas)
/// para que los módulos no repitan el mismo código una y otra vez.
/// </summary>
public abstract class SnowflakeModuleBase : ApplicationCommandModule
{
    /// <summary>Responde a la interacción con un texto plano.</summary>
    protected static async Task ResponderAsync(
        InteractionContext ctx, string contenido, bool ephemeral = false)
    {
        var builder = new DiscordInteractionResponseBuilder().WithContent(contenido);
        if (ephemeral) builder.AsEphemeral();
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, builder);
    }

    /// <summary>Responde a la interacción con un embed.</summary>
    protected static async Task ResponderAsync(
        InteractionContext ctx, DiscordEmbedBuilder embed, bool ephemeral = false)
    {
        var builder = new DiscordInteractionResponseBuilder().AddEmbed(embed);
        if (ephemeral) builder.AsEphemeral();
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, builder);
    }

    /// <summary>Responde con un mensaje de error efímero (solo lo ve quien ejecutó el comando).</summary>
    protected static Task ResponderErrorAsync(InteractionContext ctx, string mensaje)
        => ResponderAsync(ctx, $"{BotEmojis.Error} {mensaje}", ephemeral: true);

    /// <summary>
    /// Edita la respuesta diferida con un texto; si el webhook ya expiró (o la
    /// respuesta se borró), traga la excepción: no hay nada más que hacer.
    /// </summary>
    protected static async Task SafeEditAsync(InteractionContext ctx, string contenido)
    {
        try { await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(contenido)); }
        catch { /* webhook expirado o mensaje inalcanzable */ }
    }

    /// <summary>Versión embed de <see cref="SafeEditAsync(InteractionContext, string)"/>.</summary>
    protected static async Task SafeEditAsync(InteractionContext ctx, DiscordEmbedBuilder embed)
    {
        try { await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed)); }
        catch { /* webhook expirado o mensaje inalcanzable */ }
    }
}
