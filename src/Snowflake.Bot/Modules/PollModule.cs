using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Microsoft.Extensions.Logging;
using Snowflake.Bot.Services;
using Snowflake.Bot.Services.Settings;

namespace Snowflake.Bot.Modules;

public class PollModule : SnowflakeModuleBase
{
    private readonly PollWidgetService _pollService;
    private readonly GuildSettingsService _settings;
    private readonly MessagesService _msg;
    private readonly ILogger<PollModule> _logger;

    public PollModule(
        PollWidgetService pollService,
        GuildSettingsService settings,
        MessagesService msg,
        ILogger<PollModule> logger)
    {
        _pollService = pollService;
        _settings = settings;
        _msg = msg;
        _logger = logger;
    }

    [SlashCommand("poll", "Create a poll (up to 10 options)")]
    [NameLocalization(Localization.Spanish, "encuesta")]
    [NameLocalization(Localization.Portuguese, "enquete")]
    [DescriptionLocalization(Localization.Spanish, "Crea una encuesta (máximo 10 opciones)")]
    [DescriptionLocalization(Localization.Portuguese, "Cria uma enquete (máximo 10 opções)")]
    public async Task EncuestaAsync(InteractionContext ctx,
        [Option("question", "The poll question")]
        [NameLocalization(Localization.Spanish, "pregunta")]
        [NameLocalization(Localization.Portuguese, "pergunta")]
        [DescriptionLocalization(Localization.Spanish, "La pregunta de la encuesta")]
        [DescriptionLocalization(Localization.Portuguese, "A pergunta da enquete")] string pregunta,
        [Option("options", "Options separated by commas (max 10)")]
        [NameLocalization(Localization.Spanish, "opciones")]
        [NameLocalization(Localization.Portuguese, "opções")]
        [DescriptionLocalization(Localization.Spanish, "Opciones separadas por coma (máximo 10)")]
        [DescriptionLocalization(Localization.Portuguese, "Opções separadas por vírgula (máximo 10)")] string opcionesStr,
        [Option("minutes", "Minutes to auto-close (0 for manual)")]
        [NameLocalization(Localization.Spanish, "minutos")]
        [NameLocalization(Localization.Portuguese, "minutos")]
        [DescriptionLocalization(Localization.Spanish, "Minutos para cerrar automáticamente (0 para manual)")]
        [DescriptionLocalization(Localization.Portuguese, "Minutos para fechar automaticamente (0 para manual)")] double minutos = 0,
        [Option("multi_option", "Allow multiple selections")]
        [NameLocalization(Localization.Spanish, "multi_opcion")]
        [NameLocalization(Localization.Portuguese, "multiplas_opcoes")]
        [DescriptionLocalization(Localization.Spanish, "Permitir múltiple selección")]
        [DescriptionLocalization(Localization.Portuguese, "Permitir múltiplas seleções")] bool multiOpcion = false)
    {
        var opciones = opcionesStr.Split(',')
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrEmpty(x))
            .ToList();

        if (opciones.Count < 2)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Encuestas:ErrorMinOpciones"));
            return;
        }

        if (opciones.Count > 10)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Encuestas:ErrorMaxOpciones"));
            return;
        }

        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);

        var embed = new DiscordEmbedBuilder()
            .WithTitle("📊 " + pregunta)
            .WithColor(DiscordColor.Azure);

        string desc = _msg.Get(ctx.Guild.Id, "Encuestas:Opciones") + "\n\n";
        for (int i = 0; i < opciones.Count; i++)
        {
            desc += $"{PollWidgetService.NumberEmojis[i]} {opciones[i]}\n";
        }

        if (minutos > 0)
        {
            var endTime = DateTimeOffset.UtcNow.AddMinutes(minutos);
            desc += $"\n⏳ {_msg.Get(ctx.Guild.Id, "Encuestas:TerminaEn")} <t:{endTime.ToUnixTimeSeconds()}:R>";
        }

        if (multiOpcion)
        {
            desc += $"\nℹ️ {_msg.Get(ctx.Guild.Id, "Encuestas:MultiOpcion")}";
        }

        embed.WithDescription(desc);

        var builder = new DiscordWebhookBuilder().AddEmbed(embed);
        
        // Add manual end button if no time limit, or even if there is one (the user requested "o ambas" potentially, but we'll add it anyway for the author)
        builder.AddComponents(new DiscordButtonComponent(ButtonStyle.Danger, "poll_end", _msg.Get(ctx.Guild.Id, "Encuestas:FinalizarBtn")));

        var msg = await ctx.EditResponseAsync(builder);

        // Número de encuesta de este servidor (persistente) + ID del mensaje en el pie.
        var cfg = await _settings.UpdateAsync(ctx.Guild.Id, c => c.PollCount++);
        embed.WithFooter(_msg.Get(ctx.Guild.Id, "Encuestas:Footer",
            ("autor", ctx.User.Username), ("id", msg.Id.ToString()), ("numero", cfg.PollCount)));

        await msg.ModifyAsync(new DiscordMessageBuilder()
            .AddEmbed(embed)
            .AddComponents(new DiscordButtonComponent(ButtonStyle.Danger, "poll_end", _msg.Get(ctx.Guild.Id, "Encuestas:FinalizarBtn"))));

        await _pollService.RegistrarEncuestaAsync(msg, ctx.User.Id, pregunta, opciones, multiOpcion, (int)Math.Round(minutos));
    }

    [SlashCommand("polls", "List active polls in this server")]
    [NameLocalization(Localization.Spanish, "encuestas")]
    [NameLocalization(Localization.Portuguese, "enquetes")]
    [DescriptionLocalization(Localization.Spanish, "Lista las encuestas activas en este servidor")]
    [DescriptionLocalization(Localization.Portuguese, "Lista as enquetes ativas neste servidor")]
    public async Task PollsAsync(InteractionContext ctx)
    {
        var activas = _pollService.ObtenerActivas(ctx.Guild.Id);

        if (activas.Count == 0)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Encuestas:ListaVacia"), ephemeral: true);
            return;
        }

        var embed = new DiscordEmbedBuilder()
            .WithTitle(_msg.Get(ctx.Guild.Id, "Encuestas:ListaTitulo"))
            .WithColor(DiscordColor.Azure);

        foreach (var poll in activas)
        {
            var opcionesTexto = string.Join(", ", poll.Options.Take(3));
            if (poll.Options.Count > 3) opcionesTexto += $" (+{poll.Options.Count - 3})";

            embed.AddField(
                $"`{poll.MessageId}` - {poll.Question}",
                $"{_msg.Get(ctx.Guild.Id, "Encuestas:Opciones")} {opcionesTexto}\n" +
                $"{_msg.Get(ctx.Guild.Id, "Encuestas:MultiOpcionLabel")}: {(poll.MultiOption ? "✅" : "❌")}",
                inline: false);
        }

        await ResponderAsync(ctx, embed);
    }

    [SlashCommand("poll-result", "View poll results by message ID")]
    [NameLocalization(Localization.Spanish, "resultado-encuesta")]
    [NameLocalization(Localization.Portuguese, "resultado-enquete")]
    [DescriptionLocalization(Localization.Spanish, "Muestra el resultado de una encuesta por su ID de mensaje")]
    [DescriptionLocalization(Localization.Portuguese, "Mostra o resultado de uma enquete pelo ID da mensagem")]
    public async Task PollResultAsync(
        InteractionContext ctx,
        [Option("message_id", "Message ID of the poll results")]
        [NameLocalization(Localization.Spanish, "id_mensaje")]
        [NameLocalization(Localization.Portuguese, "id_mensagem")]
        [DescriptionLocalization(Localization.Spanish, "ID del mensaje con los resultados")]
        [DescriptionLocalization(Localization.Portuguese, "ID da mensagem com os resultados")]
        string messageIdStr)
    {
        if (!ulong.TryParse(messageIdStr, out var messageId))
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Encuestas:IdInvalido"));
            return;
        }

        if (!_pollService.TryObtenerCanalFinalizada(messageId, out var channelId))
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Encuestas:ResultadoNoEncontrado"));
            return;
        }

        try
        {
            var channel = await ctx.Client.GetChannelAsync(channelId);
            var msg = await channel.GetMessageAsync(messageId);

            var builder = new DiscordWebhookBuilder().WithContent(msg.Content);
            foreach (var embed in msg.Embeds)
            {
                builder.AddEmbed(embed);
            }

            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent(msg.Content).AddEmbeds(msg.Embeds));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener mensaje de encuesta {MessageId}", messageId);
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Encuestas:ErrorAlObtener"));
        }
    }
}
