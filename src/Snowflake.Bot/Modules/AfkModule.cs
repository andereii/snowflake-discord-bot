using System.Text;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Snowflake.Bot.Services;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Módulo de estado de ausencia (/afk).
/// Permite a los usuarios marcar su estado AFK y a los administradores gestionar canales ignorados y ausencias.
/// </summary>
[SlashCommandGroup("afk", "Manage AFK (Away From Keyboard) status")]
[DescriptionLocalization(Localization.Spanish, "Gestiona el estado de ausencia (AFK)")]
[DescriptionLocalization(Localization.Portuguese, "Gerencia o estado de ausência (AFK)")]
public sealed class AfkModule : SnowflakeModuleBase
{
    private readonly AfkService _afk;
    private readonly MessagesService _msg;

    public AfkModule(AfkService afk, MessagesService msg)
    {
        _afk = afk;
        _msg = msg;
    }

    [SlashCommand("set", "Set your AFK status with an optional reason")]
    [NameLocalization(Localization.Spanish, "set")]
    [NameLocalization(Localization.Portuguese, "set")]
    [DescriptionLocalization(Localization.Spanish, "Establece tu estado ausente con un motivo opcional")]
    [DescriptionLocalization(Localization.Portuguese, "Define seu estado ausente com um motivo opcional")]
    public async Task SetAsync(
        InteractionContext ctx,
        [Option("reason", "Reason for being AFK")]
        [NameLocalization(Localization.Spanish, "motivo")]
        [NameLocalization(Localization.Portuguese, "motivo")]
        [DescriptionLocalization(Localization.Spanish, "Motivo de la ausencia")]
        [DescriptionLocalization(Localization.Portuguese, "Motivo da ausência")] string? motivo = null)
    {
        var miembro = ctx.Member ?? await ctx.Guild.GetMemberAsync(ctx.User.Id);
        await _afk.EstablecerAfkAsync(ctx.Guild, miembro, motivo);

        var motivoFmt = string.IsNullOrWhiteSpace(motivo) ? "AFK" : motivo.Trim();
        var resp = _msg.Get(ctx.Guild.Id, "Afk:Establecido",
            ("usuario", ctx.User.Username),
            ("motivo", motivoFmt));

        await ResponderAsync(ctx, $"💤 {resp}");
    }

    [SlashCommand("list", "Show the list of currently AFK members")]
    [NameLocalization(Localization.Spanish, "list")]
    [NameLocalization(Localization.Portuguese, "list")]
    [DescriptionLocalization(Localization.Spanish, "Muestra la lista de miembros ausentes")]
    [DescriptionLocalization(Localization.Portuguese, "Mostra a lista de membros ausentes")]
    public async Task ListAsync(InteractionContext ctx)
    {
        var ausentes = _afk.ListarAfk(ctx.Guild.Id);
        if (ausentes.Count == 0)
        {
            await ResponderAsync(ctx, $"ℹ️ {_msg.Get(ctx.Guild.Id, "Afk:SinMiembrosAusentes")}", ephemeral: true);
            return;
        }

        var ahora = DateTimeOffset.UtcNow;
        var sb = new StringBuilder();
        foreach (var a in ausentes)
        {
            var tiempoFmt = DurationParser.Format(ahora - a.SetAt, _msg.Locale(ctx.Guild.Id));
            sb.AppendLine($"• <@{a.UserId}> — *\"{a.Reason}\"* (`{tiempoFmt}`)");
        }

        var embed = new DiscordEmbedBuilder()
            .WithTitle($"💤 {_msg.Get(ctx.Guild.Id, "Afk:TituloMiembrosAusentes")}")
            .WithDescription(sb.ToString())
            .WithColor(DiscordColor.CornflowerBlue)
            .WithFooter($"Total: {ausentes.Count}");

        await ResponderAsync(ctx, embed);
    }

    [SlashCommand("ignore", "Add a channel where AFK status won't be removed")]
    [NameLocalization(Localization.Spanish, "ignore")]
    [NameLocalization(Localization.Portuguese, "ignore")]
    [DescriptionLocalization(Localization.Spanish, "Añade un canal donde no removeré el estado de ausencia")]
    [DescriptionLocalization(Localization.Portuguese, "Adiciona um canal onde o estado de ausência não será removido")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task IgnoreAsync(
        InteractionContext ctx,
        [Option("channel", "Channel to ignore")]
        [NameLocalization(Localization.Spanish, "canal")]
        [NameLocalization(Localization.Portuguese, "canal")]
        [DescriptionLocalization(Localization.Spanish, "Canal a ignorar")]
        [DescriptionLocalization(Localization.Portuguese, "Canal a ignorar")]
        [ChannelTypes(ChannelType.Text)] DiscordChannel canal)
    {
        var agregado = await _afk.AgregarCanalIgnoradoAsync(ctx.Guild.Id, canal.Id);
        if (agregado)
        {
            var resp = _msg.Get(ctx.Guild.Id, "Afk:CanalIgnorado", ("canal", canal.Mention));
            await ResponderAsync(ctx, $"✅ {resp}");
        }
        else
        {
            var resp = _msg.Get(ctx.Guild.Id, "Afk:CanalYaIgnorado", ("canal", canal.Mention));
            await ResponderAsync(ctx, $"ℹ️ {resp}", ephemeral: true);
        }
    }

    [SlashCommand("unignore", "Remove a channel from the AFK ignored list")]
    [NameLocalization(Localization.Spanish, "unignore")]
    [NameLocalization(Localization.Portuguese, "unignore")]
    [DescriptionLocalization(Localization.Spanish, "Remueve un canal de la lista de ignorados")]
    [DescriptionLocalization(Localization.Portuguese, "Remove um canal da lista de ignorados")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task UnignoreAsync(
        InteractionContext ctx,
        [Option("channel", "Channel to unignore")]
        [NameLocalization(Localization.Spanish, "canal")]
        [NameLocalization(Localization.Portuguese, "canal")]
        [DescriptionLocalization(Localization.Spanish, "Canal a remover de la lista de ignorados")]
        [DescriptionLocalization(Localization.Portuguese, "Canal a remover da lista de ignorados")]
        [ChannelTypes(ChannelType.Text)] DiscordChannel canal)
    {
        var removido = await _afk.RemoverCanalIgnoradoAsync(ctx.Guild.Id, canal.Id);
        if (removido)
        {
            var resp = _msg.Get(ctx.Guild.Id, "Afk:CanalDesignorado", ("canal", canal.Mention));
            await ResponderAsync(ctx, $"✅ {resp}");
        }
        else
        {
            var resp = _msg.Get(ctx.Guild.Id, "Afk:CanalNoIgnorado", ("canal", canal.Mention));
            await ResponderAsync(ctx, $"⚠️ {resp}", ephemeral: true);
        }
    }

    [SlashCommand("ignored", "Show the list of AFK ignored channels")]
    [NameLocalization(Localization.Spanish, "ignored")]
    [NameLocalization(Localization.Portuguese, "ignored")]
    [DescriptionLocalization(Localization.Spanish, "Muestra la lista de canales ignorados")]
    [DescriptionLocalization(Localization.Portuguese, "Mostra a lista de canais ignorados")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task IgnoredAsync(InteractionContext ctx)
    {
        var canales = _afk.ObtenerCanalesIgnorados(ctx.Guild.Id);
        if (canales.Count == 0)
        {
            await ResponderAsync(ctx, $"ℹ️ {_msg.Get(ctx.Guild.Id, "Afk:SinCanalesIgnorados")}", ephemeral: true);
            return;
        }

        var sb = new StringBuilder();
        foreach (var cId in canales)
        {
            sb.AppendLine($"• <#{cId}> (`{cId}`)");
        }

        var embed = new DiscordEmbedBuilder()
            .WithTitle($"🔇 {_msg.Get(ctx.Guild.Id, "Afk:TituloCanalesIgnorados")}")
            .WithDescription(sb.ToString())
            .WithColor(DiscordColor.CornflowerBlue)
            .WithFooter($"Total: {canales.Count}");

        await ResponderAsync(ctx, embed);
    }

    [SlashCommand("remove", "Remove a member from the AFK list")]
    [NameLocalization(Localization.Spanish, "remove")]
    [NameLocalization(Localization.Portuguese, "remove")]
    [DescriptionLocalization(Localization.Spanish, "Remueve un miembro de la lista de ausentes")]
    [DescriptionLocalization(Localization.Portuguese, "Remove um membro da lista de ausentes")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task RemoveAsync(
        InteractionContext ctx,
        [Option("user", "Member to remove from AFK")]
        [NameLocalization(Localization.Spanish, "usuario")]
        [NameLocalization(Localization.Portuguese, "usuario")]
        [DescriptionLocalization(Localization.Spanish, "Miembro a remover de ausentes")]
        [DescriptionLocalization(Localization.Portuguese, "Membro a remover dos ausentes")] DiscordUser usuario)
    {
        var miembro = usuario as DiscordMember ?? await ctx.Guild.GetMemberAsync(usuario.Id);
        var removido = await _afk.RemoverAfkAsync(ctx.Guild, miembro);

        if (removido)
        {
            var resp = _msg.Get(ctx.Guild.Id, "Afk:RemovidoMod", ("usuario", miembro.DisplayName));
            await ResponderAsync(ctx, $"✅ {resp}");
        }
        else
        {
            var resp = _msg.Get(ctx.Guild.Id, "Afk:NoEstaAusente", ("usuario", miembro.DisplayName));
            await ResponderAsync(ctx, $"⚠️ {resp}", ephemeral: true);
        }
    }

    [SlashCommand("removeall", "Remove all members from the AFK list")]
    [NameLocalization(Localization.Spanish, "removeall")]
    [NameLocalization(Localization.Portuguese, "removeall")]
    [DescriptionLocalization(Localization.Spanish, "Remueve a todos los miembros de la lista de ausentes")]
    [DescriptionLocalization(Localization.Portuguese, "Remove todos os membros da lista de ausentes")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task RemoveAllAsync(InteractionContext ctx)
    {
        var total = await _afk.RemoverTodosAfkAsync(ctx.Guild);
        if (total > 0)
        {
            var resp = _msg.Get(ctx.Guild.Id, "Afk:RemovidosTodos", ("total", total.ToString()));
            await ResponderAsync(ctx, $"✅ {resp}");
        }
        else
        {
            await ResponderAsync(ctx, $"ℹ️ {_msg.Get(ctx.Guild.Id, "Afk:SinMiembrosAusentes")}", ephemeral: true);
        }
    }

    [SlashCommand("reset", "Reset an AFK member's reason to default")]
    [NameLocalization(Localization.Spanish, "reset")]
    [NameLocalization(Localization.Portuguese, "reset")]
    [DescriptionLocalization(Localization.Spanish, "Elimina el motivo de ausencia de un miembro")]
    [DescriptionLocalization(Localization.Portuguese, "Redefine o motivo de ausência de um membro")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task ResetAsync(
        InteractionContext ctx,
        [Option("user", "Member whose AFK reason will be reset")]
        [NameLocalization(Localization.Spanish, "usuario")]
        [NameLocalization(Localization.Portuguese, "usuario")]
        [DescriptionLocalization(Localization.Spanish, "Miembro al que se le restablecerá el motivo")]
        [DescriptionLocalization(Localization.Portuguese, "Membro que terá o motivo redefinido")] DiscordUser usuario)
    {
        var miembro = usuario as DiscordMember ?? await ctx.Guild.GetMemberAsync(usuario.Id);
        var reseteado = await _afk.ResetearMotivoAfkAsync(ctx.Guild.Id, usuario.Id);

        if (reseteado)
        {
            var resp = _msg.Get(ctx.Guild.Id, "Afk:MotivoReseteado", ("usuario", miembro.DisplayName));
            await ResponderAsync(ctx, $"✅ {resp}");
        }
        else
        {
            var resp = _msg.Get(ctx.Guild.Id, "Afk:NoEstaAusente", ("usuario", miembro.DisplayName));
            await ResponderAsync(ctx, $"⚠️ {resp}", ephemeral: true);
        }
    }
}
