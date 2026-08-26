using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.EventArgs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snowflake.Bot.Configuration;
using Snowflake.Bot.Modules;
using Snowflake.Bot.Services.AiCommands;
using Snowflake.Bot.Services.PrefixCommands;
using Snowflake.Bot.Services.Settings;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Services;

/// <summary>
/// Servicio principal: mantiene la conexión con Discord, registra los comandos
/// slash y envía el mensaje de presentación al unirse a un servidor nuevo.
/// </summary>
public sealed class DiscordBotService : BackgroundService
{
    private readonly DiscordClient _client;
    private readonly MessagesService _msg;
    private readonly GuildSettingsService _settings;
    private readonly IOptionsMonitor<BotConfiguration> _config;
    private readonly ColorService _color;
    private readonly VoiceHubService _voces;
    private readonly MusicWidgetService _musicWidget;
    private readonly ImageSearchWidgetService _imgWidget;
    private readonly PollWidgetService _pollWidget;
    private readonly DeepSeekService _ia;
    private readonly AiCommandConfirmation _confirmaciones;
    private readonly CountingService _counting;
    private readonly AfkService _afk;
    private readonly PrefixCommandService _prefix;
    private readonly ILogger<DiscordBotService> _logger;

    public DiscordBotService(
        DiscordClient client,
        MessagesService msg,
        GuildSettingsService settings,
        IServiceProvider services,
        IOptionsMonitor<BotConfiguration> config,
        ColorService color,
        VoiceHubService voces,
        MusicWidgetService musicWidget,
        ImageSearchWidgetService imgWidget,
        PollWidgetService pollWidget,
        DeepSeekService ia,
        AiCommandConfirmation confirmaciones,
        CountingService counting,
        AfkService afk,
        PrefixCommandService prefix,
        ILogger<DiscordBotService> logger)
    {
        _client = client;
        _msg = msg;
        _settings = settings;
        _config = config;
        _color = color;
        _voces = voces;
        _musicWidget = musicWidget;
        _imgWidget = imgWidget;
        _pollWidget = pollWidget;
        _ia = ia;
        _confirmaciones = confirmaciones;
        _counting = counting;
        _afk = afk;
        _prefix = prefix;
        _logger = logger;

        if (config.CurrentValue.TestGuildId == 0)
        {
            throw new InvalidOperationException(
                "TestGuildId no está configurado. Revisa la sección \"Bot\" de appsettings.json.");
        }

        _client.Ready += OnReady;
        _client.GuildDownloadCompleted += OnGuildDownloadCompleted;
        _client.GuildCreated += OnGuildCreated;
        _client.GuildMemberAdded += OnGuildMemberAdded;
        _client.VoiceStateUpdated += _voces.OnVoiceStateUpdatedAsync;
        _client.ComponentInteractionCreated += OnComponentInteractionCreated;
        _client.MessageReactionAdded += (c, e) => _pollWidget.ManejarReaccionAgregadaAsync(e);
        _client.MessageReactionRemoved += (c, e) => _pollWidget.ManejarReaccionRemovidaAsync(e);
        _client.MessageCreated += OnMessageCreated;
        _client.MessageCreated += OnMessageCreatedCounting;

        // Los comandos se registran solo en el servidor de pruebas: aparecen al instante.
        // (El registro global puede tardar hasta 1 hora en propagarse.)
        var slash = _client.UseSlashCommands(new SlashCommandsConfiguration { Services = services });
        slash.SlashCommandErrored += OnSlashCommandErrored;

        // Los comandos se registran SOLO en el servidor de pruebas para que aparezcan al instante y no se dupliquen.
        slash.RegisterCommands(typeof(DiscordBotService).Assembly, config.CurrentValue.TestGuildId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Conectando con Discord...");
        await _client.ConnectAsync();

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Apagado solicitado (Ctrl+C o parada del host).
        }

        _logger.LogInformation("Desconectando de Discord...");
        await _client.DisconnectAsync();
    }

    private async Task OnReady(DiscordClient sender, ReadyEventArgs e)
    {
        _logger.LogInformation("Sesión iniciada como {User}", sender.CurrentUser);

        // Purga cualquier comando global residual en Discord para eliminar duplicados en los servidores.
        try
        {
            await sender.BulkOverwriteGlobalApplicationCommandsAsync(Array.Empty<DiscordApplicationCommand>());
            _logger.LogInformation("Comandos globales purgados con éxito.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudieron purgar los comandos globales.");
        }
    }

    private async Task OnGuildDownloadCompleted(DiscordClient sender, GuildDownloadCompletedEventArgs e)
    {
        _logger.LogInformation("Bot listo. Servidores conectados: {Count}", sender.Guilds.Count);

        // Precarga el estado del modo espontáneo en la caché en memoria, para no
        // tocar la BD en cada mensaje del chat.
        try
        {
            foreach (var guildId in sender.Guilds.Keys)
            {
                var cfg = await _settings.GetAsync(guildId);
                _ia.EstablecerEspontaneo(guildId, cfg.GeminiSpontaneousEnabled);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo precargar el estado del modo espontáneo");
        }
    }

    /// <summary>
    /// Enruta las interacciones de componentes (menús de selección, botones…)
    /// según su custom_id: el menú de colores y el widget de música.
    /// </summary>
    private async Task OnComponentInteractionCreated(
        DiscordClient sender, ComponentInteractionCreateEventArgs e)
    {
        if (e.Id == ColorService.CustomId)
            await _color.HandleSelectAsync(e);
        else if (MusicWidgetService.EsInteraccionMusica(e.Id))
            await _musicWidget.HandleButtonAsync(e);
        else if (ImageSearchWidgetService.EsInteraccion(e.Id))
            await _imgWidget.ManejarBotonAsync(e);
        else if (AiCommandConfirmation.EsInteraccionConfirmacion(e.Id))
            await _confirmaciones.ManejarBotonAsync(e);
        else if (e.Id == "poll_end")
        {
            bool success = await _pollWidget.IntentarFinalizarManualAsync(e.Message, e.User, e.Guild);
            if (success) await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            else await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DSharpPlus.Entities.DiscordInteractionResponseBuilder().WithContent("❌ No tienes permiso para finalizar esta encuesta.").AsEphemeral());
        }
    }

    /// <summary>
    /// Responde automáticamente cuando un usuario responde a un mensaje que
    /// DeepSeek generó con /talk, o cuando lo menciona con @ (si el servidor
    /// activó las menciones). Los mensajes del propio bot se ignoran para
    /// evitar bucles de respuestas.
    /// </summary>
    private async Task OnMessageCreated(DiscordClient sender, MessageCreateEventArgs e)
    {
        if (e.Guild is null || e.Author.IsBot)
            return;

        var texto = e.Message.Content?.Trim();
        if (string.IsNullOrWhiteSpace(texto))
            return;

        // Procesa retorno de AFK y menciones a ausentes
        await _afk.ProcesarMensajeAsync(sender, e);

        // Camino 0: comando de texto con prefijo ';'
        if (await _prefix.ProcesarMensajeAsync(e))
            return;

        // Camino 1: respuesta a un mensaje del chatbot (ya existente).
        var mensajeReferenciado = e.Message.ReferencedMessage;
        if (mensajeReferenciado is not null
            && _ia.TryObtenerGuildDeMensajeGenerado(mensajeReferenciado.Id, out var guildId)
            && guildId == e.Guild.Id)
        {
            await ResponderChatAsync(e, texto, guildId);
            return;
        }

        // Camino 2: mención al bot con @ (si el servidor lo activó).
        if (MencionaAlBot(sender, e.Message))
        {
            var cfg = await _settings.GetAsync(e.Guild.Id);
            if (!cfg.GeminiMentionsEnabled) return;

            // Quitamos la mención del texto antes de enviar a DeepSeek.
            var limpio = LimpiarMencion(sender, texto);
            if (string.IsNullOrWhiteSpace(limpio)) return;

            await ResponderChatAsync(e, limpio, e.Guild.Id);
            return;
        }

        // Camino 3: chismorreo espontáneo. Solo en mensajes ambientales (no
        // son respuesta al bot ni mención). Se cuentan para el umbral del
        // servidor; si toca, se dispara un comentario en background.
        if (_ia.EspontaneoHabilitado(e.Guild.Id))
        {
            // Ignoramos comandos (no aportan contexto de charla).
            if (texto.StartsWith('/') || texto.StartsWith(PrefixCommandService.Prefijo)) return;

            var dispara = _ia.RegistrarMensajeParaEspontaneo(e.Guild.Id, e.Author.Username, texto);
            if (dispara)
            {
                _ = DispararComentarioEspontaneoAsync(e.Guild.Id, e.Channel);
            }
        }
    }

    /// <summary>
    /// Pide a DeepSeek un comentario espontáneo a partir de la conversación
    /// reciente del canal y lo envía a dicho canal. Se ejecuta en background
    /// (fire-and-forget) para no frenar el procesamiento de mensajes.
    /// </summary>
    private async Task DispararComentarioEspontaneoAsync(ulong guildId, DiscordChannel canal)
    {
        try
        {
            var recientes = _ia.ObtenerRecientes(guildId);
            if (recientes.Count == 0) return;

            var respuesta = await _ia.GenerarComentarioEspontaneoAsync(guildId, recientes);
            var contenido = ChatResponseFormatter.Formatear(respuesta, _msg.Get(guildId, "Chat:Truncada"));
            await canal.SendMessageAsync(contenido);
        }
        catch (DeepSeekException ex)
        {
            _logger.LogInformation(
                ex, "No se pudo generar comentario espontáneo en {Guild}", guildId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Error inesperado en comentario espontáneo en {Guild}", guildId);
        }
    }

    /// <summary>Genera la respuesta de DeepSeek como reply a <paramref name="e"/>.</summary>
    private async Task ResponderChatAsync(MessageCreateEventArgs e, string texto, ulong guildId)
    {
        DiscordMessage? mensajeBot = null;
        try
        {
            // Enviamos algo inmediatamente para que el usuario vea que la
            // solicitud fue recibida mientras el modelo genera la respuesta.
            mensajeBot = await e.Message.RespondAsync(
                new DiscordMessageBuilder().WithContent(_msg.Get(guildId, "Chat:Pensando")));

            var miembro = e.Message.Author as DiscordMember
                ?? await e.Guild!.GetMemberAsync(e.Author.Id);
            var aiCtx = new AiCommandContext(_client, e.Guild!, e.Channel, miembro);

            var outcome = await _ia.PreguntarAsync(aiCtx, e.Author.Username, texto);

            if (outcome.HayPendiente)
            {
                // Comando destructivo: eliminamos el mensaje inicial de 'Pensando...' y enviamos la confirmación.
                try { await mensajeBot.DeleteAsync(); } catch { }
                await _confirmaciones.EnviarNormalAsync(e.Channel, outcome.Pendiente!, aiCtx, outcome.Pendiente!.DescripcionComando);
                return;
            }

            var contenido = ChatResponseFormatter.Formatear(outcome.Texto ?? "", _msg.Get(guildId, "Chat:Truncada"));

            var builder = new DiscordMessageBuilder().WithContent(contenido);
            foreach (var comando in outcome.Comandos)
                builder.AddEmbed(ChatModule.ConstruirEmbedComando(comando));

            await mensajeBot.ModifyAsync(builder);
            _ia.RegistrarMensajeGenerado(mensajeBot.Id, guildId);
        }
        catch (DeepSeekBusyException ex)
        {
            _logger.LogInformation(
                ex,
                "Se rechazó una solicitud de chat por límite de concurrencia en {Guild}/{Channel}",
                e.Guild!.Id,
                e.Channel.Id);
            await ModificarRespuestaChatAsync(mensajeBot, e.Message, _msg.Get(guildId, "Chat:Ocupado"));
        }
        catch (DeepSeekConfirmationPendingException)
        {
            await ModificarRespuestaChatAsync(mensajeBot, e.Message, _msg.Get(guildId, "Chat:ConfirmacionEnCurso"));
        }
        catch (DeepSeekException ex)
        {
            _logger.LogWarning(
                ex,
                "No se pudo responder automáticamente en {Guild}/{Channel}",
                e.Guild!.Id,
                e.Channel.Id);

            await ModificarRespuestaChatAsync(mensajeBot, e.Message, _msg.Get(guildId, "Chat:Error"));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error procesando una respuesta automática en {Guild}/{Channel}",
                e.Guild!.Id,
                e.Channel.Id);

            await ModificarRespuestaChatAsync(mensajeBot, e.Message, _msg.Get(guildId, "Chat:Error"));
        }
    }

    /// <summary>Comprueba si <paramref name="m"/> menciona al bot (por texto &lt;@id&gt; o &lt;!@id&gt;).</summary>
    private static bool MencionaAlBot(DiscordClient c, DiscordMessage m)
    {
        var botId = c.CurrentUser.Id;
        var content = m.Content;
        return content is not null
            && (content.Contains($"<@{botId}>") || content.Contains($"<@!{botId}>"));
    }

    /// <summary>Elimina del texto la mención al bot (formatos &lt;@id&gt; y &lt;!@id&gt;).</summary>
    private static string LimpiarMencion(DiscordClient c, string texto)
    {
        var id = c.CurrentUser.Id;
        return texto
            .Replace($"<@!{id}>", " ")
            .Replace($"<@{id}>", " ")
            .Replace($"<@&{id}>", " ") // rol mention, por si acaso
            .Trim();
    }

    /// <summary>
    /// Procesa los mensajes del canal de conteo (si el servidor lo configuró).
    /// Handler separado del de Gemini: ambos conviven en el evento MessageCreated.
    /// </summary>
    private Task OnMessageCreatedCounting(DiscordClient sender, MessageCreateEventArgs e)
        => _counting.ProcesarMensajeAsync(e.Message);

    private static async Task ModificarRespuestaChatAsync(
        DiscordMessage? mensajeBot,
        DiscordMessage mensajeUsuario,
        string contenido)
    {
        try
        {
            if (mensajeBot is not null)
                await mensajeBot.ModifyAsync(new DiscordMessageBuilder().WithContent(contenido));
            else
                await mensajeUsuario.RespondAsync(new DiscordMessageBuilder().WithContent(contenido));
        }
        catch
        {
            // El canal puede haber sido borrado o el bot no tener permiso.
        }
    }

    /// <summary>
    /// Al unirse a un servidor nuevo, envía la presentación con recomendaciones
    /// al canal de sistema (o al primer canal de texto donde pueda escribir).
    /// </summary>
    private async Task OnGuildCreated(DiscordClient sender, GuildCreateEventArgs e)
    {
        try
        {
            var canal = EncontrarCanalPresentacion(e.Guild);
            if (canal is null)
            {
                _logger.LogWarning(
                    "No hay ningún canal donde presentarse en {Guild} ({Id})", e.Guild.Name, e.Guild.Id);
                return;
            }

            var embed = new DiscordEmbedBuilder()
                .WithTitle(_msg.Get(e.Guild.Id, "Presentacion:Titulo", ("bot", sender.CurrentUser.Username)))
                .WithDescription(_msg.Get(e.Guild.Id, "Presentacion:Descripcion", ("servidor", e.Guild.Name)))
                .WithColor(DiscordColor.Azure)
                .AddField(
                    _msg.Get(e.Guild.Id, "Presentacion:RecomendacionesTitulo"),
                    _msg.Get(e.Guild.Id, "Presentacion:RecomendacionesTexto"))
                .WithFooter(_msg.Get(e.Guild.Id, "Presentacion:Pie"));

            await canal.SendMessageAsync(embed.Build());
            _logger.LogInformation(
                "Presentación enviada a #{Canal} del servidor {Guild}", canal.Name, e.Guild.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo enviar la presentación en el servidor {Guild}", e.Guild.Id);
        }
    }

    /// <summary>
    /// Cuando un miembro entra al servidor, le da la bienvenida en el canal configurado.
    /// </summary>
    private async Task OnGuildMemberAdded(DiscordClient sender, GuildMemberAddEventArgs e)
    {
        try
        {
            if (e.Member.IsBot) return;

            var config = await _settings.GetAsync(e.Guild.Id);
            if (config.WelcomeChannelId is not ulong canalId) return;

            var canal = e.Guild.GetChannel(canalId);
            if (canal is null)
            {
                _logger.LogWarning("El canal de bienvenida {Canal} ya no existe en {Guild}", canalId, e.Guild.Id);
                return;
            }

            // Mensaje personalizado guardado por el servidor, o el por defecto del bot.
            var texto = string.IsNullOrWhiteSpace(config.WelcomeMessage)
                ? _msg.Get(e.Guild.Id, "Bienvenida:MensajePorDefecto",
                    ("usuario", e.Member.Mention), ("servidor", e.Guild.Name))
                : config.WelcomeMessage!
                    .Replace("{usuario}", e.Member.Mention)
                    .Replace("{servidor}", e.Guild.Name);

            var embed = new DiscordEmbedBuilder()
                .WithTitle(_msg.Get(e.Guild.Id, "Bienvenida:Titulo"))
                .WithDescription(texto)
                .WithColor(DiscordColor.Azure)
                .WithThumbnail(e.Member.AvatarUrl ?? e.Member.DefaultAvatarUrl)
                .WithFooter(_msg.Get(e.Guild.Id, "Bienvenida:Pie", ("servidor", e.Guild.Name)))
                .WithTimestamp(DateTimeOffset.UtcNow);

            await canal.SendMessageAsync(embed.Build());
            _logger.LogInformation("Bienvenida enviada a {Miembro} en {Guild}", e.Member.Id, e.Guild.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando la bienvenida en {Guild}", e.Guild.Id);
        }
    }

    /// <summary>
    /// Devuelve el canal de sistema si el bot puede escribir en él;
    /// si no, el primer canal de texto (por posición) donde tenga permiso.
    /// </summary>
    private static DiscordChannel? EncontrarCanalPresentacion(DiscordGuild guild)
    {
        var bot = guild.CurrentMember;

        if (guild.SystemChannel is { } sistema && PuedeEscribir(sistema, bot))
            return sistema;

        return guild.Channels.Values
            .Where(c => c.Type == ChannelType.Text && PuedeEscribir(c, bot))
            .OrderBy(c => c.Position)
            .FirstOrDefault();
    }

    private static bool PuedeEscribir(DiscordChannel canal, DiscordMember bot) =>
        canal.PermissionsFor(bot).HasPermission(Permissions.SendMessages);

    /// <summary>
    /// Maneja los errores de los comandos slash: permisos insuficientes o excepciones.
    /// </summary>
    private async Task OnSlashCommandErrored(SlashCommandsExtension sender, SlashCommandErrorEventArgs e)
    {
        _logger.LogError(e.Exception, "Error en el comando /{Command}", e.Context.CommandName);

        // En modo debug, se incluye el mensaje de la excepción para depurar;
        // en producción se responde de forma genérica para no filtrar detalles.
        var debug = _config.CurrentValue.Debug;
        var mensaje = e.Exception is SlashExecutionChecksFailedException
            ? _msg.Get(e.Context.Guild?.Id ?? 0, "Errores:SinPermisos")
            : debug
                ? _msg.Get(e.Context.Guild?.Id ?? 0, "Errores:InternoDebug", ("mensaje", e.Exception.Message))
                : _msg.Get(e.Context.Guild?.Id ?? 0, "Errores:Interno");

        try
        {
            await e.Context.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent(mensaje).AsEphemeral());
        }
        catch
        {
            // Si ya se había respondido (p. ej. con DeferAsync), se intenta con un follow-up.
            try
            {
                await e.Context.FollowUpAsync(
                    new DiscordFollowupMessageBuilder().WithContent(mensaje).AsEphemeral());
            }
            catch
            {
                // No se pudo notificar al usuario; el error ya quedó en el log.
            }
        }
    }
}
