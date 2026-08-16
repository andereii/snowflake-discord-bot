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
public sealed class ClearModule : SnowflakeModuleBase
{
    // Discord no permite borrado masivo de mensajes con más de 14 días.
    private static readonly TimeSpan LimiteBulk = TimeSpan.FromDays(14);

    // Pausa entre borrados individuales (mensajes viejos) para respetar rate limits.
    private static readonly TimeSpan PausaBorradoIndividual = TimeSpan.FromMilliseconds(600);

    private readonly MessagesService _msg;

    public ClearModule(MessagesService msg) => _msg = msg;

    [SlashCommand("clear", "Delete a number of messages in a channel")]
    [NameLocalization(Localization.Spanish, "clear")]
    [NameLocalization(Localization.Portuguese, "clear")]
    [DescriptionLocalization(Localization.Spanish, "Borra una cantidad de mensajes en un canal")]
    [DescriptionLocalization(Localization.Portuguese, "Apaga uma quantidade de mensagens em um canal")]
    [SlashRequirePermissions(Permissions.ManageMessages)]
    [SlashRequireBotPermissions(Permissions.ManageMessages)]
    public async Task ClearAsync(
        InteractionContext ctx,
        [Option("amount", "How many messages to delete (1-100)")]
        [NameLocalization(Localization.Spanish, "cantidad")]
        [NameLocalization(Localization.Portuguese, "quantidade")]
        [DescriptionLocalization(Localization.Spanish, "Cuántos mensajes borrar (1-100)")]
        [DescriptionLocalization(Localization.Portuguese, "Quantas mensagens apagar (1-100)")] long cantidad,
        [Option("channel", "Channel to delete in (empty = this channel)")]
        [NameLocalization(Localization.Spanish, "canal")]
        [NameLocalization(Localization.Portuguese, "canal")]
        [DescriptionLocalization(Localization.Spanish, "Canal donde borrar (vacío = este canal)")]
        [DescriptionLocalization(Localization.Portuguese, "Canal para apagar (vazio = este canal)")] DiscordChannel? canal = null)
    {
        canal ??= ctx.Channel;
        if (canal.Type != ChannelType.Text)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Limpiar:CanalDebeSerTexto"), ephemeral: true);
            return;
        }
        if (cantidad < 1 || cantidad > 100)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Limpiar:SinCantidad"), ephemeral: true);
            return;
        }

        // Auditoría: los atributos de permiso comprueban el permiso global, pero
        // los overrides del canal destino pueden quitárselo a quien ejecuta o al
        // bot. Verificamos ambos sobre el canal concreto antes de tocar nada.
        var miembro = ctx.Member ?? await ctx.Guild.GetMemberAsync(ctx.User.Id);
        if (miembro is not null
            && !canal.PermissionsFor(miembro).HasPermission(Permissions.ManageMessages))
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Limpiar:SinPermisosCanal"));
            return;
        }

        var bot = ctx.Guild.CurrentMember;
        if (bot is not null
            && !canal.PermissionsFor(bot).HasPermission(Permissions.ManageMessages))
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Limpiar:SinPermisosBotCanal"));
            return;
        }

        await ctx.DeferAsync(ephemeral: true);

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
                await Task.Delay(PausaBorradoIndividual);
            }

            // Respuesta según haya habido mensajes viejos excluidos.
            string resultado;
            if (viejos.Count > 0 && borrables.Count > 0)
            {
                resultado = _msg.Get(ctx.Guild.Id, "Limpiar:ExitoExcluido",
                    ("n", borrados),
                    ("canal", canal.Mention),
                    ("excluidos", viejos.Count));
            }
            else if (borrados == 0)
            {
                resultado = _msg.Get(ctx.Guild.Id, "Limpiar:SinMensajes", ("canal", canal.Mention));
            }
            else
            {
                resultado = _msg.Get(ctx.Guild.Id, "Limpiar:Exito", ("n", borrados), ("canal", canal.Mention));
            }

            // La respuesta diferida es efímera para no ensuciar el canal recién limpiado y se borra a los 3s.
            await SafeEditAsync(ctx, resultado);
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                try { await ctx.DeleteResponseAsync(); }
                catch { /* ignorar */ }
            });
        }
        catch (Exception)
        {
            await SafeEditAsync(ctx, _msg.Get(ctx.Guild.Id, "Errores:Interno"));
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                try { await ctx.DeleteResponseAsync(); }
                catch { /* ignorar */ }
            });
        }
    }
}
