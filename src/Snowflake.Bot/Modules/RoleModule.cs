using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Snowflake.Bot.Services;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Comandos slash para asignación y remoción de roles a miembros del servidor.
/// </summary>
[SlashCommandGroup("role", "Manage user roles")]
[NameLocalization(Localization.Spanish, "rol")]
[NameLocalization(Localization.Portuguese, "cargo")]
[DescriptionLocalization(Localization.Spanish, "Gestiona roles de usuarios")]
[DescriptionLocalization(Localization.Portuguese, "Gerencia cargos de usuários")]
public sealed class RoleModule : SnowflakeModuleBase
{
    private readonly MessagesService _msg;

    public RoleModule(MessagesService msg)
    {
        _msg = msg;
    }

    [SlashCommand("add", "Add a role to a user")]
    [NameLocalization(Localization.Spanish, "agregar")]
    [NameLocalization(Localization.Portuguese, "adicionar")]
    [DescriptionLocalization(Localization.Spanish, "Añade un rol a un usuario")]
    [DescriptionLocalization(Localization.Portuguese, "Adiciona um cargo a um usuário")]
    [SlashRequirePermissions(Permissions.ManageRoles)]
    [SlashRequireBotPermissions(Permissions.ManageRoles)]
    public async Task AddAsync(
        InteractionContext ctx,
        [Option("user", "User to receive the role")]
        [NameLocalization(Localization.Spanish, "usuario")]
        [NameLocalization(Localization.Portuguese, "usuario")]
        [DescriptionLocalization(Localization.Spanish, "Usuario al que se le dará el rol")]
        [DescriptionLocalization(Localization.Portuguese, "Usuário que receberá o cargo")] DiscordUser usuario,
        [Option("role", "Role to add")]
        [NameLocalization(Localization.Spanish, "rol")]
        [NameLocalization(Localization.Portuguese, "cargo")]
        [DescriptionLocalization(Localization.Spanish, "Rol a asignar")]
        [DescriptionLocalization(Localization.Portuguese, "Cargo a atribuir")] DiscordRole rol)
    {
        var miembro = usuario as DiscordMember ?? await ctx.Guild.GetMemberAsync(usuario.Id);
        if (miembro is null)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Moderacion:NoMiembro"));
            return;
        }

        if (ctx.Guild.CurrentMember.Hierarchy <= rol.Position)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Roles:JerarquiaBot", ("rol", rol.Name)));
            return;
        }

        if (ctx.Guild.OwnerId != ctx.Member.Id && ctx.Member.Hierarchy <= rol.Position)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Roles:JerarquiaUsuario", ("rol", rol.Name)));
            return;
        }

        if (miembro.Roles.Any(r => r.Id == rol.Id))
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Roles:YaTiene", ("usuario", miembro.DisplayName), ("rol", rol.Name)));
            return;
        }

        await miembro.GrantRoleAsync(rol, $"Asignado por {ctx.User.Username} ({ctx.User.Id})");
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Roles:Asignado", ("usuario", miembro.DisplayName), ("rol", rol.Name)));
    }

    [SlashCommand("remove", "Remove a role from a user")]
    [NameLocalization(Localization.Spanish, "quitar")]
    [NameLocalization(Localization.Portuguese, "remover")]
    [DescriptionLocalization(Localization.Spanish, "Quita un rol a un usuario")]
    [DescriptionLocalization(Localization.Portuguese, "Remove um cargo de um usuário")]
    [SlashRequirePermissions(Permissions.ManageRoles)]
    [SlashRequireBotPermissions(Permissions.ManageRoles)]
    public async Task RemoveAsync(
        InteractionContext ctx,
        [Option("user", "User to remove the role from")]
        [NameLocalization(Localization.Spanish, "usuario")]
        [NameLocalization(Localization.Portuguese, "usuario")]
        [DescriptionLocalization(Localization.Spanish, "Usuario al que se le quitará el rol")]
        [DescriptionLocalization(Localization.Portuguese, "Usuário de quem o cargo será removido")] DiscordUser usuario,
        [Option("role", "Role to remove")]
        [NameLocalization(Localization.Spanish, "rol")]
        [NameLocalization(Localization.Portuguese, "cargo")]
        [DescriptionLocalization(Localization.Spanish, "Rol a quitar")]
        [DescriptionLocalization(Localization.Portuguese, "Cargo a remover")] DiscordRole rol)
    {
        var miembro = usuario as DiscordMember ?? await ctx.Guild.GetMemberAsync(usuario.Id);
        if (miembro is null)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Moderacion:NoMiembro"));
            return;
        }

        if (ctx.Guild.CurrentMember.Hierarchy <= rol.Position)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Roles:JerarquiaBot", ("rol", rol.Name)));
            return;
        }

        if (ctx.Guild.OwnerId != ctx.Member.Id && ctx.Member.Hierarchy <= rol.Position)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, "Roles:JerarquiaUsuario", ("rol", rol.Name)));
            return;
        }

        if (!miembro.Roles.Any(r => r.Id == rol.Id))
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Roles:NoTiene", ("usuario", miembro.DisplayName), ("rol", rol.Name)));
            return;
        }

        await miembro.RevokeRoleAsync(rol, $"Quitado por {ctx.User.Username} ({ctx.User.Id})");
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Roles:Removido", ("usuario", miembro.DisplayName), ("rol", rol.Name)));
    }
}
