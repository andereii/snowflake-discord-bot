using System.Collections.Concurrent;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using Microsoft.Extensions.Logging;
using Snowflake.Bot.Services.AiCommands;

namespace Snowflake.Bot.Services;

/// <summary>
/// Confirmación de comandos destructivos propuestos por la IA: mensaje (efímero
/// si viene de /talk, normal si viene de una mención) con botones Aceptar/Rechazar
/// y rechazo automático a los 15 segundos sin respuesta. Solo el usuario que
/// pidió el comando puede responder.
/// </summary>
public sealed class AiCommandConfirmation(
    DiscordClient client,
    DeepSeekService ia,
    AiCommandExecutor executor,
    MessagesService msg,
    ILogger<AiCommandConfirmation> logger)
{
    public const string PrefijoCustomId = "snowflake_ai_confirm_";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private sealed record Estado(
        PendingCommand Pendiente,
        AiCommandContext Ctx,
        ulong UsuarioId,
        string ComandoDescripcion)
    {
        public DiscordInteraction? Interaction { get; init; }
        public ulong? MensajeEfimeroId { get; init; }
        public DiscordMessage? MensajePublico { get; init; }
    }

    private readonly ConcurrentDictionary<string, Estado> _pendientes = new();

    /// <summary>¿El custom_id pertenece a un botón de confirmación de la IA?</summary>
    public static bool EsInteraccionConfirmacion(string customId)
        => customId.StartsWith(PrefijoCustomId, StringComparison.Ordinal);

    /// <summary>Mensaje de pre-texto + confirmación efímera (camino /talk).</summary>
    public async Task EnviarEfimeroAsync(
        InteractionContext ctx, PendingCommand pendiente, AiCommandContext aiCtx, string descripcion)
    {
        var builder = CrearConfirmacionSeguimiento(aiCtx.Guild.Id, descripcion, pendiente.Token);
        DiscordMessage? mensaje = null;
        try
        {
            mensaje = await ctx.FollowUpAsync(builder);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo enviar la confirmación efímera en {Guild}", aiCtx.Guild.Id);
        }

        var estado = new Estado(pendiente, aiCtx, ctx.User.Id, descripcion)
        {
            Interaction = ctx.Interaction,
            MensajeEfimeroId = mensaje?.Id
        };
        _pendientes[pendiente.Token] = estado;
        _ = ExpirarAsync(pendiente.Token);
    }

    /// <summary>Confirmación como mensaje normal del canal (camino de menciones).</summary>
    public async Task EnviarNormalAsync(
        DiscordChannel canal, PendingCommand pendiente, AiCommandContext aiCtx, string descripcion)
    {
        var builder = CrearConfirmacion(aiCtx.Guild.Id, descripcion, pendiente.Token);
        DiscordMessage? mensaje = null;
        try
        {
            mensaje = await canal.SendMessageAsync(builder);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo enviar la confirmación en {Guild}", aiCtx.Guild.Id);
        }

        var estado = new Estado(pendiente, aiCtx, aiCtx.Miembro.Id, descripcion)
        {
            MensajePublico = mensaje
        };
        _pendientes[pendiente.Token] = estado;
        _ = ExpirarAsync(pendiente.Token);
    }

    /// <summary>Maneja el clic en Aceptar/Rechazar.</summary>
    public async Task ManejarBotonAsync(ComponentInteractionCreateEventArgs e)
    {
        // custom_id: snowflake_ai_confirm_{token}_ok | _no
        var partes = e.Id.Split('_');
        if (partes.Length < 2) return;
        var token = partes[^2];
        var aceptar = e.Id.EndsWith("_ok", StringComparison.Ordinal);

        // Ack inmediato para no agotar la ventana de interacción.
        try
        {
            await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
        }
        catch { /* ya respondida */ }

        if (!_pendientes.TryRemove(token, out var estado))
        {
            await ResponderEfimeroAsync(e, msg.Get(e.Guild!.Id, "Chat:ComandoExpirado"));
            return;
        }

        if (e.User.Id != estado.UsuarioId)
        {
            // Devolver el estado para que siga esperando al usuario correcto.
            _pendientes[token] = estado;
            await ResponderEfimeroAsync(e, msg.Get(e.Guild!.Id, "Chat:ConfirmacionSoloSolicitante"));
            return;
        }

        await ResolverAsync(estado, aceptar, expirado: false);
    }

    // ------------------------- internos -------------------------

    private DiscordEmbed CrearEmbedConfirmacion(ulong guildId, string descripcion) =>
        new DiscordEmbedBuilder()
            .WithTitle(msg.Get(guildId, "Chat:ConfirmacionTitulo"))
            .WithDescription(msg.Get(guildId, "Chat:ConfirmacionTexto", ("comando", descripcion)))
            .WithColor(DiscordColor.Gold);

    private DiscordButtonComponent[] CrearBotones(ulong guildId, string token) =>
    [
        new(ButtonStyle.Success, $"{PrefijoCustomId}{token}_ok", msg.Get(guildId, "Chat:ConfirmacionAceptar")),
        new(ButtonStyle.Danger, $"{PrefijoCustomId}{token}_no", msg.Get(guildId, "Chat:ConfirmacionRechazar"))
    ];

    private DiscordMessageBuilder CrearConfirmacion(ulong guildId, string descripcion, string token) =>
        new DiscordMessageBuilder()
            .AddEmbed(CrearEmbedConfirmacion(guildId, descripcion))
            .AddComponents(CrearBotones(guildId, token));

    private DiscordFollowupMessageBuilder CrearConfirmacionSeguimiento(ulong guildId, string descripcion, string token) =>
        new DiscordFollowupMessageBuilder()
            .AddEmbed(CrearEmbedConfirmacion(guildId, descripcion))
            .AddComponents(CrearBotones(guildId, token))
            .AsEphemeral();

    private async Task ExpirarAsync(string token)
    {
        await Task.Delay(Timeout).ConfigureAwait(false);
        if (!_pendientes.TryRemove(token, out var estado)) return; // ya resuelto
        await ResolverAsync(estado, aceptar: false, expirado: true).ConfigureAwait(false);
    }

    /// <summary>Deshabilita los botones del mensaje de confirmación.</summary>
    private async Task DeshabilitarBotonesAsync(Estado estado)
    {
        try
        {
            if (estado.Interaction is not null && estado.MensajeEfimeroId is { } idEfimero)
            {
                var builder = new DiscordWebhookBuilder()
                    .AddComponents(
                        new DiscordButtonComponent(ButtonStyle.Success, "snowflake_ai_done_ok", "✓", disabled: true),
                        new DiscordButtonComponent(ButtonStyle.Danger, "snowflake_ai_done_no", "✕", disabled: true));
                await estado.Interaction.EditFollowupMessageAsync(idEfimero, builder);
            }
            else if (estado.MensajePublico is not null)
            {
                await estado.MensajePublico.ModifyAsync(new DiscordMessageBuilder()
                    .WithContent(msg.Get(estado.Ctx.Guild.Id, "Chat:ComandoCancelado"))
                    .AddComponents(
                        new DiscordButtonComponent(ButtonStyle.Success, "snowflake_ai_done_ok", "✓", disabled: true),
                        new DiscordButtonComponent(ButtonStyle.Danger, "snowflake_ai_done_no", "✕", disabled: true)));
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "No se pudo deshabilitar la confirmación en {Guild}", estado.Ctx.Guild.Id);
        }
    }

    /// <summary>Ejecuta (o rechaza) y deja que el modelo termine la respuesta.</summary>
    private async Task ResolverAsync(Estado estado, bool aceptar, bool expirado)
    {
        var guildId = estado.Ctx.Guild.Id;
        await DeshabilitarBotonesAsync(estado).ConfigureAwait(false);

        AiChatOutcome outcome;

        try
        {
            if (!aceptar)
            {
                var rechazo = expirado
                    ? msg.Get(guildId, "Chat:ComandoExpirado")
                    : msg.Get(guildId, "Chat:ComandoCancelado");
                // El modelo termina la frase según el resultado.
                outcome = await ia.ReanudarToolAsync(
                    estado.Ctx, estado.Pendiente,
                    "The user did not authorize the command. Acknowledge briefly.",
                    default).ConfigureAwait(false);
                await PublicarAsync(estado, outcome, extra: null, aviso: rechazo).ConfigureAwait(false);
                return;
            }

            // Ejecuta el comando REAL (permisos re-validados) y reanuda al modelo.
            var ejecucion = await executor.EjecutarAsync(
                estado.Ctx, estado.Pendiente.ToolName, estado.Pendiente.Args).ConfigureAwait(false);
            var resultado = ejecucion.Resultado
                ?? new AiCommandResult(false, msg.Get(guildId, "Chat:ErrorEjecucion"), estado.Pendiente.DescripcionComando);

            outcome = await ia.ReanudarToolAsync(
                estado.Ctx, estado.Pendiente, resultado.Texto, default).ConfigureAwait(false);
            await PublicarAsync(estado, outcome, extra: resultado, aviso: null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error resolviendo la confirmación de IA en {Guild}", guildId);
            try
            {
                await PublicarAsync(estado, new AiChatOutcome { Texto = msg.Get(guildId, "Chat:ComandoCancelado") },
                    extra: null, aviso: msg.Get(guildId, "Chat:ErrorEjecucion")).ConfigureAwait(false);
            }
            catch { /* último intento */ }
        }
    }

    /// <summary>Publica el mensaje final: texto del modelo + embed(s) con el output de los comandos.</summary>
    private async Task PublicarAsync(
        Estado estado, AiChatOutcome outcome, AiCommandResult? extra, string? aviso)
    {
        var guildId = estado.Ctx.Guild.Id;
        var texto = string.IsNullOrWhiteSpace(outcome.Texto)
            ? aviso ?? msg.Get(guildId, "Chat:ComandoCancelado")
            : outcome.Texto!;

        var builder = new DiscordMessageBuilder().WithContent(texto);

        if (extra is not null)
            builder.AddEmbed(CrearEmbedOutput(guildId, extra));

        foreach (var comando in outcome.Comandos)
            builder.AddEmbed(CrearEmbedOutput(guildId, comando));

        try
        {
            var mensaje = await estado.Ctx.Canal.SendMessageAsync(builder);
            ia.RegistrarMensajeGenerado(mensaje.Id, guildId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo publicar el resultado de la confirmación en {Guild}", guildId);
        }
    }

    private DiscordEmbed CrearEmbedOutput(ulong guildId, AiCommandResult comando)
    {
        var embed = new DiscordEmbedBuilder()
            .WithTitle(comando.Descripcion)
            .WithDescription(comando.Texto)
            .WithColor(comando.Exitoso ? DiscordColor.Green : DiscordColor.Red);
        return embed.Build();
    }

    private static async Task ResponderEfimeroAsync(ComponentInteractionCreateEventArgs e, string texto)
    {
        try
        {
            await e.Interaction.CreateFollowupMessageAsync(
                new DiscordFollowupMessageBuilder().WithContent(texto).AsEphemeral());
        }
        catch { /* ventana cerrada */ }
    }
}
