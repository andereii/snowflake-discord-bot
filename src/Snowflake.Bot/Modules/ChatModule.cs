using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Microsoft.Extensions.Options;
using Snowflake.Bot.Configuration;
using Snowflake.Bot.Services;
using Snowflake.Bot.Services.Settings;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Chatbot con Gemini. <c>/charlar</c> usa una conversación compartida por
/// todos los usuarios del servidor; <c>/charlar-limpiar</c> la reinicia.
/// <c>/gemini-menciones</c> y <c>/gemini-espontaneo</c> activan los modos extra.
/// </summary>
public sealed class ChatModule : SnowflakeModuleBase
{
    private readonly GeminiService _gemini;
    private readonly GuildSettingsService _settings;
    private readonly MessagesService _msg;
    private readonly IOptionsMonitor<BotConfiguration> _config;

    public ChatModule(
        GeminiService gemini,
        GuildSettingsService settings,
        MessagesService msg,
        IOptionsMonitor<BotConfiguration> config)
    {
        _gemini = gemini;
        _settings = settings;
        _msg = msg;
        _config = config;
    }

    [SlashCommand("charlar", "Habla con la IA en la conversación compartida del servidor")]
    public async Task CharlarAsync(
        InteractionContext ctx,
        [Option("texto", "Lo que quieres decirle o preguntarle")] string texto)
    {
        // Interruptor por servidor (desactivable desde el panel de configuración).
        if (!(await _settings.GetAsync(ctx.Guild.Id)).GeminiChatEnabled)
        {
            await ResponderAsync(ctx, _msg.Get("Chat:Desactivado"), ephemeral: true);
            return;
        }

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
    [SlashRequirePermissions(Permissions.ManageGuild)]
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

            await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.GeminiMentionsEnabled = valor);
            await ResponderAsync(ctx,
                valor
                    ? _msg.Get("Chat:MencionesActivadas")
                    : _msg.Get("Chat:MencionesDesactivadas"));
        }
        else
        {
            var cfg = await _settings.GetAsync(ctx.Guild.Id);
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

            await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.GeminiSpontaneousEnabled = valor);
            _gemini.EstablecerEspontaneo(ctx.Guild.Id, valor); // actualiza la caché en caliente
            await ResponderAsync(ctx,
                valor
                    ? _msg.Get("Chat:EspontaneoActivado")
                    : _msg.Get("Chat:EspontaneoDesactivado"));
        }
        else
        {
            var cfg = await _settings.GetAsync(ctx.Guild.Id);
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
}
