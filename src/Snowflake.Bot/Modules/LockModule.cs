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

    [SlashCommand("lock", "Lock a channel: nobody will be able to talk in it (lockdown)")]
    [NameLocalization(Localization.Spanish, "bloquear")]
    [NameLocalization(Localization.Portuguese, "bloquear")]
    [DescriptionLocalization(Localization.Spanish, "Bloquea un canal: nadie podrá hablar en él (lockdown)")]
    [DescriptionLocalization(Localization.Portuguese, "Bloqueia um canal: ninguém poderá falar nele (lockdown)")]
    [SlashRequirePermissions(Permissions.ManageChannels)]
    [SlashRequireBotPermissions(Permissions.ManageRoles)]
    public async Task BloquearAsync(
        InteractionContext ctx,
        [Option("channel", "Channel to lock (empty = this channel)")]
        [NameLocalization(Localization.Spanish, "canal")]
        [NameLocalization(Localization.Portuguese, "canal")]
        [DescriptionLocalization(Localization.Spanish, "Canal a bloquear (vacío = este canal)")]
        [DescriptionLocalization(Localization.Portuguese, "Canal a bloquear (vazio = este canal)")] DiscordChannel? canal = null,
        [Option("reason", "Reason for the lockdown")]
        [NameLocalization(Localization.Spanish, "motivo")]
        [NameLocalization(Localization.Portuguese, "motivo")]
        [DescriptionLocalization(Localization.Spanish, "Motivo del bloqueo")]
        [DescriptionLocalization(Localization.Portuguese, "Motivo do bloqueio")] string? motivo = null)
    {
        canal ??= ctx.Channel;
        motivo ??= _msg.Get(ctx.Guild.Id, "Moderacion:MotivoPorDefecto");

        if (!EsCanalValido(canal))
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Bloqueo:CanalInvalido"));
            return;
        }

        // Auditoría: los atributos comprueban el permiso global; verificamos
        // también los overrides del canal destino (usuario y bot).
        var miembro = ctx.Member ?? await ctx.Guild.GetMemberAsync(ctx.User.Id);
        if (miembro is not null
            && !canal.PermissionsFor(miembro).HasPermission(Permissions.ManageChannels))
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Bloqueo:SinPermisosCanal", ("canal", canal.Mention)));
            return;
        }

        // Cambiar overwrites exige "Gestionar roles" en el canal concreto.
        var bot = ctx.Guild.CurrentMember;
        if (bot is not null
            && !canal.PermissionsFor(bot).HasPermission(Permissions.ManageRoles))
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Bloqueo:SinPermisosBotCanal", ("canal", canal.Mention)));
            return;
        }

        var aplicado = await _locks.BloquearAsync(canal, motivo);
        var texto = aplicado
            ? _msg.Get(ctx.Guild.Id, "Bloqueo:Bloqueado", ("canal", canal.Mention))
            : _msg.Get(ctx.Guild.Id, "Bloqueo:YaBloqueado", ("canal", canal.Mention));
        await ResponderAsync(ctx, texto, ephemeral: !aplicado);
    }

    [SlashCommand("unlock", "Unlock a channel: restore the previous permissions")]
    [NameLocalization(Localization.Spanish, "desbloquear")]
    [NameLocalization(Localization.Portuguese, "desbloquear")]
    [DescriptionLocalization(Localization.Spanish, "Desbloquea un canal: restaura los permisos anteriores")]
    [DescriptionLocalization(Localization.Portuguese, "Desbloqueia um canal: restaura as permissões anteriores")]
    [SlashRequirePermissions(Permissions.ManageChannels)]
    [SlashRequireBotPermissions(Permissions.ManageRoles)]
    public async Task DesbloquearAsync(
        InteractionContext ctx,
        [Option("channel", "Channel to unlock (empty = this channel)")]
        [NameLocalization(Localization.Spanish, "canal")]
        [NameLocalization(Localization.Portuguese, "canal")]
        [DescriptionLocalization(Localization.Spanish, "Canal a desbloquear (vacío = este canal)")]
        [DescriptionLocalization(Localization.Portuguese, "Canal a desbloquear (vazio = este canal)")] DiscordChannel? canal = null,
        [Option("reason", "Reason for the unlock")]
        [NameLocalization(Localization.Spanish, "motivo")]
        [NameLocalization(Localization.Portuguese, "motivo")]
        [DescriptionLocalization(Localization.Spanish, "Motivo del desbloqueo")]
        [DescriptionLocalization(Localization.Portuguese, "Motivo do desbloqueio")] string? motivo = null)
    {
        canal ??= ctx.Channel;
        motivo ??= _msg.Get(ctx.Guild.Id, "Moderacion:MotivoPorDefecto");

        if (!EsCanalValido(canal))
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Bloqueo:CanalInvalido"));
            return;
        }

        var miembro = ctx.Member ?? await ctx.Guild.GetMemberAsync(ctx.User.Id);
        if (miembro is not null
            && !canal.PermissionsFor(miembro).HasPermission(Permissions.ManageChannels))
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Bloqueo:SinPermisosCanal", ("canal", canal.Mention)));
            return;
        }

        var bot = ctx.Guild.CurrentMember;
        if (bot is not null
            && !canal.PermissionsFor(bot).HasPermission(Permissions.ManageRoles))
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Bloqueo:SinPermisosBotCanal", ("canal", canal.Mention)));
            return;
        }

        var aplicado = await _locks.DesbloquearAsync(canal, motivo);
        var texto = aplicado
            ? _msg.Get(ctx.Guild.Id, "Bloqueo:Desbloqueado", ("canal", canal.Mention))
            : _msg.Get(ctx.Guild.Id, "Bloqueo:NoBloqueado", ("canal", canal.Mention));
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
