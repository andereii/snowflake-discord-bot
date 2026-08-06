using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Snowflake.Bot.Services;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Limpieza de mensajes en un canal. Borrado masivo de Discord (hasta 100
/// mensajes) o individual si los mensajes superan los 14 días.
/// </summary>
public sealed class ClearModule : ApplicationCommandModule
{
    // Discord no permite borrado masivo de mensajes con más de 14 días.
    private static readonly TimeSpan LimiteBulk = TimeSpan.FromDays(14);

    private readonly MessagesService _msg;

    public ClearModule(MessagesService msg) => _msg = msg;

    [SlashCommand("clear", "Borra una cantidad de mensajes en un canal")]
    [SlashRequirePermissions(Permissions.ManageMessages)]
    [SlashRequireBotPermissions(Permissions.ManageMessages)]
    public async Task ClearAsync(
        InteractionContext ctx,
        [Option("cantidad", "Cuántos mensajes borrar (1-100)")] long cantidad,
        [Option("canal", "Canal donde borrar (vacío = este canal)")] DiscordChannel? canal = null)
    {
        canal ??= ctx.Channel;
        if (canal.Type != ChannelType.Text)
        {
            await ResponderAsync(ctx, _msg.Get("Limpiar:CanalDebeSerTexto"), ephemeral: true);
            return;
        }
        if (cantidad < 1 || cantidad > 100)
        {
            await ResponderAsync(ctx, _msg.Get("Limpiar:SinCantidad"), ephemeral: true);
            return;
        }

        await ctx.DeferAsync();

        try
        {
            var mensajes = await canal.GetMessagesAsync((int)cantidad);

            // Discord solo permite borrado masivo de mensajes < 14 días.
            var ahora = DateTimeOffset.UtcNow;
            var borrables = new List<DiscordMessage>();
            var viejos = new List<DiscordMessage>();

            if (mensajes is not null)
            {
                foreach (var m in mensajes)
                {
                    if (m.Id == ctx.Channel.LastMessageId && m.Author?.Id == ctx.Client.CurrentUser.Id)
                    {
                        // No intentamos borrar el mensaje de la propia interacción
                        // diferida (el webhook); seguirá editándose más abajo.
                    }

                    if ((ahora - m.CreationTimestamp) < LimiteBulk)
                        borrables.Add(m);
                    else
                        viejos.Add(m);
                }
            }

            var borrados = 0;
            if (borrables.Count > 0)
            {
                // DeleteMessagesAsync exige al menos 2 mensajes; si solo hay 1,
                // cae al borrado individual.
                if (borrables.Count == 1)
                {
                    try
                    {
                        await canal.DeleteMessageAsync(borrables[0], "/clear");
                        borrados++;
                    }
                    catch { /* mensaje ya borrado */ }
                }
                else
                {
                    await canal.DeleteMessagesAsync(borrables, "/clear");
                    borrados += borrables.Count;
                }
            }

            // Mensajes viejos (> 14 días): borrado individual con pausa para
            // respetar los rate limits de Discord.
            foreach (var m in viejos)
            {
                try { await canal.DeleteMessageAsync(m, "/clear"); borrados++; }
                catch { /* mensaje ya borrado */ }
                await Task.Delay(600);
            }

            // Respuesta según haya habido mensajes viejos excluidos.
            string resultado;
            if (viejos.Count > 0 && borrables.Count > 0)
            {
                resultado = _msg.Get("Limpiar:ExitoExcluido",
                    ("n", borrados),
                    ("canal", canal.Mention),
                    ("excluidos", viejos.Count));
            }
            else if (borrados == 0)
            {
                resultado = _msg.Get("Limpiar:SinMensajes", ("canal", canal.Mention));
            }
            else
            {
                resultado = _msg.Get("Limpiar:Exito", ("n", borrados), ("canal", canal.Mention));
            }

            // La respuesta diferida es efímera para no ensuciar el canal recién limpiado.
            try { await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(resultado)); }
            catch { }
        }
        catch (Exception)
        {
            try { await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(_msg.Get("Errores:Interno"))); }
            catch { }
        }
    }

    private static async Task ResponderAsync(InteractionContext ctx, string contenido, bool ephemeral = false)
    {
        var b = new DiscordInteractionResponseBuilder().WithContent(contenido);
        if (ephemeral) b.AsEphemeral();
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, b);
    }
}