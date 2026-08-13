using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Snowflake.Bot.Services;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Lockdown de canales: /bloquear impide que nadie hable en un canal (o se
/// conecte, si es de voz) y /desbloquear restaura los permisos exactos que
/// había antes. El overwrite original se guarda en BD (tabla ChannelLocks).
/// </summary>
public sealed class LockModule : SnowflakeModuleBase
{
    private readonly ChannelLockService _locks;
    private readonly MessagesService _msg;

    public LockModule(ChannelLockService locks, MessagesService msg)
    {
        _locks = locks;
        _msg = msg;
    }

    [SlashCommand("bloquear", "Bloquea un canal: nadie podrá hablar en él (lockdown)")]
    [SlashRequirePermissions(Permissions.ManageChannels)]
    [SlashRequireBotPermissions(Permissions.ManageRoles)]
    public async Task BloquearAsync(
        InteractionContext ctx,
        [Option("canal", "Canal a bloquear (vacío = este canal)")] DiscordChannel? canal = null,
        [Option("motivo", "Motivo del bloqueo")] string? motivo = null)
    {
        canal ??= ctx.Channel;
        motivo ??= _msg.Get("Moderacion:MotivoPorDefecto");

        if (!EsCanalValido(canal))
        {
            await ResponderErrorAsync(ctx, _msg.Get("Bloqueo:CanalInvalido"));
            return;
        }

        // Auditoría: los atributos comprueban el permiso global; verificamos
        // también los overrides del canal destino (usuario y bot).
        var miembro = ctx.Member ?? await ctx.Guild.GetMemberAsync(ctx.User.Id);
        if (miembro is not null
            && !canal.PermissionsFor(miembro).HasPermission(Permissions.ManageChannels))
        {
            await ResponderErrorAsync(ctx, _msg.Get("Bloqueo:SinPermisosCanal", ("canal", canal.Mention)));
            return;
        }

        // Cambiar overwrites exige "Gestionar roles" en el canal concreto.
        var bot = ctx.Guild.CurrentMember;
        if (bot is not null
            && !canal.PermissionsFor(bot).HasPermission(Permissions.ManageRoles))
        {
            await ResponderErrorAsync(ctx, _msg.Get("Bloqueo:SinPermisosBotCanal", ("canal", canal.Mention)));
            return;
        }

        var aplicado = await _locks.BloquearAsync(canal, motivo);
        var texto = aplicado
            ? _msg.Get("Bloqueo:Bloqueado", ("canal", canal.Mention))
            : _msg.Get("Bloqueo:YaBloqueado", ("canal", canal.Mention));
        await ResponderAsync(ctx, texto, ephemeral: !aplicado);
    }

    [SlashCommand("desbloquear", "Desbloquea un canal: restaura los permisos anteriores")]
    [SlashRequirePermissions(Permissions.ManageChannels)]
    [SlashRequireBotPermissions(Permissions.ManageRoles)]
    public async Task DesbloquearAsync(
        InteractionContext ctx,
        [Option("canal", "Canal a desbloquear (vacío = este canal)")] DiscordChannel? canal = null,
        [Option("motivo", "Motivo del desbloqueo")] string? motivo = null)
    {
        canal ??= ctx.Channel;
        motivo ??= _msg.Get("Moderacion:MotivoPorDefecto");

        if (!EsCanalValido(canal))
        {
            await ResponderErrorAsync(ctx, _msg.Get("Bloqueo:CanalInvalido"));
            return;
        }

        var miembro = ctx.Member ?? await ctx.Guild.GetMemberAsync(ctx.User.Id);
        if (miembro is not null
            && !canal.PermissionsFor(miembro).HasPermission(Permissions.ManageChannels))
        {
            await ResponderErrorAsync(ctx, _msg.Get("Bloqueo:SinPermisosCanal", ("canal", canal.Mention)));
            return;
        }

        var bot = ctx.Guild.CurrentMember;
        if (bot is not null
            && !canal.PermissionsFor(bot).HasPermission(Permissions.ManageRoles))
        {
            await ResponderErrorAsync(ctx, _msg.Get("Bloqueo:SinPermisosBotCanal", ("canal", canal.Mention)));
            return;
        }

        var aplicado = await _locks.DesbloquearAsync(canal, motivo);
        var texto = aplicado
            ? _msg.Get("Bloqueo:Desbloqueado", ("canal", canal.Mention))
            : _msg.Get("Bloqueo:NoBloqueado", ("canal", canal.Mention));
        await ResponderAsync(ctx, texto, ephemeral: !aplicado);
    }

    /// <summary>Solo se bloquean canales de texto (chat) o de voz.</summary>
    private static bool EsCanalValido(DiscordChannel canal) => canal.Type switch
    {
        ChannelType.Text or ChannelType.News or ChannelType.GuildForum
            or ChannelType.Voice or ChannelType.Stage => true,
        _ => false
    };
}
