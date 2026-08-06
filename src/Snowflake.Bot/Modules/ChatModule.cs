using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Snowflake.Bot.Configuration;
using Snowflake.Bot.Data;
using Snowflake.Bot.Data.Entities;
using Snowflake.Bot.Services;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Chatbot con Gemini. <c>/charlar</c> usa una conversación compartida por
/// todos los usuarios del servidor; <c>/charlar-limpiar</c> la reinicia.
/// <c>/gemini menciones</c> activa/desactiva las respuestas a menciones @.
/// </summary>
public sealed class ChatModule : ApplicationCommandModule
{
    private readonly GeminiService _gemini;
    private readonly IDbContextFactory<BotDbContext> _dbFactory;
    private readonly MessagesService _msg;
    private readonly IOptionsMonitor<BotConfiguration> _config;

    public ChatModule(
        GeminiService gemini,
        IDbContextFactory<BotDbContext> dbFactory,
        MessagesService msg,
        IOptionsMonitor<BotConfiguration> config)
    {
        _gemini = gemini;
        _dbFactory = dbFactory;
        _msg = msg;
        _config = config;
    }

    [SlashCommand("charlar", "Habla con la IA en la conversación compartida del servidor")]
    public async Task CharlarAsync(
        InteractionContext ctx,
        [Option("texto", "Lo que quieres decirle o preguntarle")] string texto)
    {
        // Respondemos inmediatamente con un mensaje visible y luego lo editamos.
        // Así la interacción queda confirmada antes de llamar a Gemini.
        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(_msg.Get("Chat:Pensando")));

        try
        {
            var nombre = ctx.Member?.DisplayName ?? ctx.User.Username;
            var respuesta = await _gemini.PreguntarAsync(ctx.Guild.Id, nombre, texto);
            var contenido = ChatResponseFormatter.Formatear(respuesta);
            await EditarYRegistrarAsync(ctx, contenido, ctx.Guild.Id);
        }
        catch (GeminiBusyException)
        {
            await SafeEditAsync(ctx, _msg.Get("Chat:Ocupado"));
        }
        catch (GeminiException ex)
        {
            var debug = _config.CurrentValue.Debug;
            var contenido = ex.Message == "Falta la variable de entorno GEMINI_API_KEY."
                ? _msg.Get("Chat:SinApiKey")
                : debug
                    ? _msg.Get("Chat:ErrorDebug", ("mensaje", ex.Message))
                    : _msg.Get("Chat:Error");
            await SafeEditAsync(ctx, contenido);
        }
        catch (Exception ex)
        {
            var contenido = _config.CurrentValue.Debug
                ? _msg.Get("Chat:ErrorDebug", ("mensaje", $"{ex.GetType().Name}: {ex.Message}"))
                : _msg.Get("Chat:Error");
            await SafeEditAsync(ctx, contenido);
        }
    }

    [SlashCommand("charlar-limpiar", "Reinicia la conversación compartida del servidor")]
    public async Task LimpiarAsync(InteractionContext ctx)
    {
        if (_gemini.Limpiar(ctx.Guild.Id))
        {
            await ResponderAsync(ctx, _msg.Get("Chat:Limpiado"));
        }
        else
        {
            await ResponderAsync(ctx, _msg.Get("Chat:SinConversacion"), ephemeral: true);
        }
    }

    [SlashCommand("gemini-menciones", "Activa o desactiva las respuestas cuando me mencionan con @")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task MencionesAsync(
        InteractionContext ctx,
        [Option("estado", "Activar o desactivar (vacío = mostrar estado actual)")]
        [Choice("Activar", "on"), Choice("Desactivar", "off")]
        string? estado = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var cfg = await db.GuildConfigs.FindAsync(ctx.Guild.Id);
        if (cfg is null)
        {
            cfg = new GuildConfig { GuildId = ctx.Guild.Id };
            db.GuildConfigs.Add(cfg);
        }

        var activar = estado switch
        {
            "on" => (bool?)true,
            "off" => (bool?)false,
            _ => null
        };

        if (activar is { } valor)
        {
            var clave = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (valor && string.IsNullOrWhiteSpace(clave))
            {
                await ResponderAsync(ctx, _msg.Get("Chat:MencionesFaltaApiKey"), ephemeral: true);
                return;
            }

            cfg.GeminiMentionsEnabled = valor;
            await db.SaveChangesAsync();
            await ResponderAsync(ctx,
                valor
                    ? _msg.Get("Chat:MencionesActivadas")
                    : _msg.Get("Chat:MencionesDesactivadas"));
        }
        else
        {
            var texto = cfg.GeminiMentionsEnabled
                ? _msg.Get("Chat:MencionesActivadas")
                : _msg.Get("Chat:MencionesDesactivadas");
            await ResponderAsync(ctx, texto, ephemeral: true);
        }
    }

    [SlashCommand("gemini-espontaneo", "Activa o desactiva que el bot hable solo en el chat (sin menciones)")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task EspontaneoAsync(
        InteractionContext ctx,
        [Option("estado", "Activar o desactivar (vacío = mostrar estado actual)")]
        [Choice("Activar", "on"), Choice("Desactivar", "off")]
        string? estado = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var cfg = await db.GuildConfigs.FindAsync(ctx.Guild.Id);
        if (cfg is null)
        {
            cfg = new GuildConfig { GuildId = ctx.Guild.Id };
            db.GuildConfigs.Add(cfg);
        }

        var activar = estado switch
        {
            "on" => (bool?)true,
            "off" => (bool?)false,
            _ => null
        };

        if (activar is { } valor)
        {
            var clave = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (valor && string.IsNullOrWhiteSpace(clave))
            {
                await ResponderAsync(ctx, _msg.Get("Chat:EspontaneoFaltaApiKey"), ephemeral: true);
                return;
            }

            cfg.GeminiSpontaneousEnabled = valor;
            await db.SaveChangesAsync();
            _gemini.EstablecerEspontaneo(ctx.Guild.Id, valor); // actualiza la caché en caliente
            await ResponderAsync(ctx,
                valor
                    ? _msg.Get("Chat:EspontaneoActivado")
                    : _msg.Get("Chat:EspontaneoDesactivado"));
        }
        else
        {
            var texto = cfg.GeminiSpontaneousEnabled
                ? _msg.Get("Chat:EspontaneoActivado")
                : _msg.Get("Chat:EspontaneoDesactivado");
            await ResponderAsync(ctx, texto, ephemeral: true);
        }
    }

    private async Task EditarYRegistrarAsync(
        InteractionContext ctx,
        string contenido,
        ulong guildId)
    {
        try
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(contenido));
            var mensaje = await ctx.GetOriginalResponseAsync();
            _gemini.RegistrarMensajeGenerado(mensaje.Id, guildId);
        }
        catch
        {
            // La respuesta ya no se puede actualizar; no se intenta revivirla.
        }
    }

    private static async Task ResponderAsync(
        InteractionContext ctx,
        string contenido,
        bool ephemeral = false)
    {
        var builder = new DiscordInteractionResponseBuilder().WithContent(contenido);
        if (ephemeral) builder.AsEphemeral();
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, builder);
    }

    /// <summary>Edita la respuesta diferida sin propagar un error de webhook expirado.</summary>
    private static async Task SafeEditAsync(InteractionContext ctx, string contenido)
    {
        try
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(contenido));
        }
        catch
        {
            // El error ya quedó registrado por el router de comandos.
        }
    }
}
