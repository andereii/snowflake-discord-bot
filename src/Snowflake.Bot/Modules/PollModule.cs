using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Snowflake.Bot.Services;

namespace Snowflake.Bot.Modules;

public class PollModule : SnowflakeModuleBase
{
    private readonly PollWidgetService _pollService;
    private readonly MessagesService _msg;

    public PollModule(PollWidgetService pollService, MessagesService msg)
    {
        _pollService = pollService;
        _msg = msg;
    }

    [SlashCommand("encuesta", "Crea una encuesta")]
    public async Task EncuestaAsync(InteractionContext ctx,
        [Option("pregunta", "La pregunta de la encuesta")] string pregunta,
        [Option("opciones", "Opciones separadas por coma (ej: Si, No, Tal vez)")] string opcionesStr,
        [Option("minutos", "Minutos para cerrar automáticamente (0 para manual)")] double minutos = 0,
        [Option("multi_opcion", "Permitir múltiple selección")] bool multiOpcion = false)
    {
        var opciones = opcionesStr.Split(',')
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrEmpty(x))
            .Take(10)
            .ToList();

        if (opciones.Count < 2)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Encuestas:ErrorMinOpciones"));
            return;
        }

        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);

        var embed = new DiscordEmbedBuilder()
            .WithTitle("📊 " + pregunta)
            .WithColor(DiscordColor.Azure)
            .WithFooter(_msg.Get(ctx.Guild.Id, "Encuestas:Footer", ("autor", ctx.User.Username)));

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

        await _pollService.RegistrarEncuestaAsync(msg, ctx.User.Id, pregunta, opciones, multiOpcion, (int)Math.Round(minutos));
    }
}
