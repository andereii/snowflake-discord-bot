using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Microsoft.Extensions.Options;
using Snowflake.Bot.Configuration;
using Snowflake.Bot.Services;
using Snowflake.Bot.Services.AiCommands;
using Snowflake.Bot.Services.Settings;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Chatbot con IA. <c>/talk</c> usa una conversación compartida por
/// todos los usuarios del servidor; <c>/charlar-limpiar</c> la reinicia.
/// <c>/ai-mentions</c> y <c>/ai-spontaneous</c> activan los modos extra.
/// </summary>
public sealed class ChatModule : SnowflakeModuleBase
{
    private readonly AiService _ia;
    private readonly GuildSettingsService _settings;
    private readonly AiCommandConfirmation _confirmaciones;
    private readonly MessagesService _msg;
    private readonly IOptionsMonitor<BotConfiguration> _config;

    public ChatModule(
        AiService ia,
        GuildSettingsService settings,
        AiCommandConfirmation confirmaciones,
        MessagesService msg,
        IOptionsMonitor<BotConfiguration> config)
    {
        _ia = ia;
        _settings = settings;
        _confirmaciones = confirmaciones;
        _msg = msg;
        _config = config;
    }

    [SlashCommand("talk", "Talk to the AI in the server's shared conversation")]
    [NameLocalization(Localization.Spanish, "charlar")]
    [NameLocalization(Localization.Portuguese, "conversar")]
    [DescriptionLocalization(Localization.Spanish, "Habla con la IA en la conversación compartida del servidor")]
    [DescriptionLocalization(Localization.Portuguese, "Fala com a IA na conversa compartilhada do servidor")]
    public async Task CharlarAsync(
        InteractionContext ctx,
        [Option("text", "What you want to say or ask")]
        [NameLocalization(Localization.Spanish, "texto")]
        [NameLocalization(Localization.Portuguese, "texto")]
        [DescriptionLocalization(Localization.Spanish, "Lo que quieres decirle o preguntarle")]
        [DescriptionLocalization(Localization.Portuguese, "O que você quer dizer ou perguntar")] string texto)
    {
        // Interruptor por servidor (desactivable desde el panel de configuración).
        if (!(await _settings.GetAsync(ctx.Guild.Id)).AiChatEnabled)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Chat:Desactivado"), ephemeral: true);
            return;
        }

        // Respondemos inmediatamente con un mensaje visible (formato blockquote)
        // y luego lo editamos. Así la interacción queda confirmada antes de llamar a la IA.
        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent("> " + _msg.Get(ctx.Guild.Id, "Chat:Pensando")));

        try
        {
            var nombre = ctx.Member?.DisplayName ?? ctx.User.Username;
            var aiCtx = new AiCommandContext(ctx.Client, ctx.Guild, ctx.Channel, ctx.Member);
            var outcome = await _ia.PreguntarAsync(aiCtx, nombre, texto);

            if (outcome.HayPendiente)
            {
                // Comando destructivo: eliminamos la respuesta pública 'Pensando...' y enviamos la confirmación efímera.
                try { await ctx.DeleteResponseAsync(); } catch { }
                await _confirmaciones.EnviarEfimeroAsync(ctx, outcome.Pendiente!, aiCtx, outcome.Pendiente!.DescripcionComando);
                return;
            }

            var contenido = ChatResponseFormatter.Formatear(outcome.Texto ?? "", _msg.Get(ctx.Guild.Id, "Chat:Truncada"));

            // Retroalimentación: si la IA usó búsqueda web, mostramos las líneas de estado.
            if (outcome.UsoBusquedaWeb)
            {
                contenido = "> " + _msg.Get(ctx.Guild.Id, "Chat:Pensando") + "\n"
                          + "> " + _msg.Get(ctx.Guild.Id, "Chat:BuscandoWeb") + "\n\n"
                          + contenido;
            }

            await EditarYRegistrarConEmbedsAsync(ctx, contenido, outcome.Comandos, ctx.Guild.Id);
        }
        catch (AiBusyException)
        {
            await SafeEditAsync(ctx, _msg.Get(ctx.Guild.Id, "Chat:Ocupado"));
        }
        catch (AiConfirmationPendingException)
        {
            await SafeEditAsync(ctx, _msg.Get(ctx.Guild.Id, "Chat:ConfirmacionEnCurso"));
        }
        catch (AiApiKeyMissingException)
        {
            await SafeEditAsync(ctx, _msg.Get(ctx.Guild.Id, "Chat:SinApiKey"));
        }
        catch (AiException ex)
        {
            var debug = _config.CurrentValue.Debug;
            var contenido = debug
                ? _msg.Get(ctx.Guild.Id, "Chat:ErrorDebug", ("mensaje", ex.Message))
                : _msg.Get(ctx.Guild.Id, "Chat:Error");
            await SafeEditAsync(ctx, contenido);
        }
        catch (Exception ex)
        {
            var contenido = _config.CurrentValue.Debug
                ? _msg.Get(ctx.Guild.Id, "Chat:ErrorDebug", ("mensaje", $"{ex.GetType().Name}: {ex.Message}"))
                : _msg.Get(ctx.Guild.Id, "Chat:Error");
            await SafeEditAsync(ctx, contenido);
        }
    }

    [SlashCommand("talk-clear", "Reset the server's shared conversation")]
    [NameLocalization(Localization.Spanish, "charlar-limpiar")]
    [NameLocalization(Localization.Portuguese, "conversar-limpar")]
    [DescriptionLocalization(Localization.Spanish, "Reinicia la conversación compartida del servidor")]
    [DescriptionLocalization(Localization.Portuguese, "Reinicia a conversa compartilhada do servidor")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task LimpiarAsync(InteractionContext ctx)
    {
        if (_ia.Limpiar(ctx.Guild.Id))
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Chat:Limpiado"));
        }
        else
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Chat:SinConversacion"), ephemeral: true);
        }
    }

    [SlashCommand("ai-mentions", "Enable or disable responses when I'm mentioned with @")]
    [NameLocalization(Localization.Spanish, "ia-menciones")]
    [NameLocalization(Localization.Portuguese, "ia-mencoes")]
    [DescriptionLocalization(Localization.Spanish, "Activa o desactiva las respuestas cuando me mencionan con @")]
    [DescriptionLocalization(Localization.Portuguese, "Ativa ou desativa as respostas quando me mencionam com @")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task MencionesAsync(
        InteractionContext ctx,
        [Option("state", "Enable or disable (empty = show current state)")]
        [NameLocalization(Localization.Spanish, "estado")]
        [NameLocalization(Localization.Portuguese, "estado")]
        [DescriptionLocalization(Localization.Spanish, "Activar o desactivar (vacío = mostrar estado actual)")]
        [DescriptionLocalization(Localization.Portuguese, "Ativar ou desativar (vazio = mostrar estado atual)")]
        [Choice("Enable", "on"), Choice("Disable", "off")]
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
            if (valor && !HayAlgunaApiKey())
            {
                await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Chat:MencionesFaltaApiKey"), ephemeral: true);
                return;
            }

            await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.AiMentionsEnabled = valor);
            await ResponderAsync(ctx,
                valor
                    ? _msg.Get(ctx.Guild.Id, "Chat:MencionesActivadas")
                    : _msg.Get(ctx.Guild.Id, "Chat:MencionesDesactivadas"));
        }
        else
        {
            var cfg = await _settings.GetAsync(ctx.Guild.Id);
            var texto = cfg.AiMentionsEnabled
                ? _msg.Get(ctx.Guild.Id, "Chat:MencionesActivadas")
                : _msg.Get(ctx.Guild.Id, "Chat:MencionesDesactivadas");
            await ResponderAsync(ctx, texto, ephemeral: true);
        }
    }

    [SlashCommand("ai-spontaneous", "Enable or disable the bot talking on its own in the chat (no mentions)")]
    [NameLocalization(Localization.Spanish, "ia-espontaneo")]
    [NameLocalization(Localization.Portuguese, "ia-espontaneo")]
    [DescriptionLocalization(Localization.Spanish, "Activa o desactiva que el bot hable solo en el chat (sin menciones)")]
    [DescriptionLocalization(Localization.Portuguese, "Ativa ou desativa o bot falar sozinho no chat (sem menções)")]
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
            if (valor && !HayAlgunaApiKey())
            {
                await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Chat:EspontaneoFaltaApiKey"), ephemeral: true);
                return;
            }

            await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.AiSpontaneousEnabled = valor);
            _ia.EstablecerEspontaneo(ctx.Guild.Id, valor); // actualiza la caché en caliente
            await ResponderAsync(ctx,
                valor
                    ? _msg.Get(ctx.Guild.Id, "Chat:EspontaneoActivado")
                    : _msg.Get(ctx.Guild.Id, "Chat:EspontaneoDesactivado"));
        }
        else
        {
            var cfg = await _settings.GetAsync(ctx.Guild.Id);
            var texto = cfg.AiSpontaneousEnabled
                ? _msg.Get(ctx.Guild.Id, "Chat:EspontaneoActivado")
                : _msg.Get(ctx.Guild.Id, "Chat:EspontaneoDesactivado");
            await ResponderAsync(ctx, texto, ephemeral: true);
        }
    }

    /// <summary>True si hay al menos una clave de API de IA configurada (DeepSeek o Gemini).</summary>
    private static bool HayAlgunaApiKey() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY"))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY"));

    [SlashCommand("ai-search", "Enable or disable the AI's internet search (the model decides when to use it)")]
    [NameLocalization(Localization.Spanish, "ia-busqueda")]
    [NameLocalization(Localization.Portuguese, "ia-busca")]
    [DescriptionLocalization(Localization.Spanish, "Activa o desactiva que la IA busque en internet cuando lo considere necesario")]
    [DescriptionLocalization(Localization.Portuguese, "Ativa ou desativa a busca na internet da IA quando ela julgar necessário")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task BusquedaAsync(
        InteractionContext ctx,
        [Option("estado", "Activar o desactivar (vacío = mostrar estado actual)")]
        [NameLocalization(Localization.Spanish, "estado")]
        [NameLocalization(Localization.Portuguese, "estado")]
        [DescriptionLocalization(Localization.Spanish, "Activar o desactivar (vacío = mostrar estado actual)")]
        [DescriptionLocalization(Localization.Portuguese, "Ativar ou desativar (vazio = mostrar estado atual)")]
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
            await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.AiWebSearchEnabled = valor);
            await ResponderAsync(ctx,
                valor
                    ? _msg.Get(ctx.Guild.Id, "Chat:BusquedaActivada")
                    : _msg.Get(ctx.Guild.Id, "Chat:BusquedaDesactivada"));
        }
        else
        {
            var cfg = await _settings.GetAsync(ctx.Guild.Id);
            var texto = cfg.AiWebSearchEnabled
                ? _msg.Get(ctx.Guild.Id, "Chat:BusquedaActivada")
                : _msg.Get(ctx.Guild.Id, "Chat:BusquedaDesactivada");
            await ResponderAsync(ctx, texto, ephemeral: true);
        }
    }

    [SlashCommand("ai-commands", "Enable or disable executing bot commands from chat instructions")]
    [NameLocalization(Localization.Spanish, "ia-comandos")]
    [NameLocalization(Localization.Portuguese, "ia-comandos")]
    [DescriptionLocalization(Localization.Spanish, "Activa o desactiva que la IA ejecute comandos del bot desde el chat")]
    [DescriptionLocalization(Localization.Portuguese, "Ativa ou desativa a IA executar comandos do bot pelo chat")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task ComandosAsync(
        InteractionContext ctx,
        [Option("estado", "Activar o desactivar (vacío = mostrar estado actual)")]
        [NameLocalization(Localization.Spanish, "estado")]
        [NameLocalization(Localization.Portuguese, "estado")]
        [DescriptionLocalization(Localization.Spanish, "Activar o desactivar (vacío = mostrar estado actual)")]
        [DescriptionLocalization(Localization.Portuguese, "Ativar ou desativar (vazio = mostrar estado atual)")]
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
            await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.AiCommandsEnabled = valor);
            await ResponderAsync(ctx,
                valor
                    ? _msg.Get(ctx.Guild.Id, "Chat:ComandosActivados")
                    : _msg.Get(ctx.Guild.Id, "Chat:ComandosDesactivados"));
        }
        else
        {
            var cfg = await _settings.GetAsync(ctx.Guild.Id);
            var texto = cfg.AiCommandsEnabled
                ? _msg.Get(ctx.Guild.Id, "Chat:ComandosActivados")
                : _msg.Get(ctx.Guild.Id, "Chat:ComandosDesactivados");
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
            _ia.RegistrarMensajeGenerado(mensaje.Id, guildId);
        }
        catch
        {
            // La respuesta ya no se puede actualizar; no se intenta revivirla.
        }
    }

    /// <summary>Edita la respuesta con el texto del modelo + embeds con el output de los comandos ejecutados.</summary>
    private async Task EditarYRegistrarConEmbedsAsync(
        InteractionContext ctx,
        string contenido,
        List<AiCommandResult> comandos,
        ulong guildId)
    {
        try
        {
            var builder = new DiscordWebhookBuilder().WithContent(contenido);
            foreach (var comando in comandos)
                builder.AddEmbed(ConstruirEmbedComando(comando));

            await ctx.EditResponseAsync(builder);
            var mensaje = await ctx.GetOriginalResponseAsync();
            _ia.RegistrarMensajeGenerado(mensaje.Id, guildId);
        }
        catch
        {
            // La respuesta ya no se puede actualizar; no se intenta revivirla.
        }
    }

    /// <summary>Embed con SOLO el output real del comando (verde/rojo según éxito).</summary>
    internal static DiscordEmbed ConstruirEmbedComando(AiCommandResult comando) =>
        new DiscordEmbedBuilder()
            .WithTitle(comando.Descripcion)
            .WithDescription(comando.Texto)
            .WithColor(comando.Exitoso ? DiscordColor.Green : DiscordColor.Red)
            .Build();
}
