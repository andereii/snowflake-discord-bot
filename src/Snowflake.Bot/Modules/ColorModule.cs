using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Snowflake.Bot.Services;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Paleta de colores: los administradores instalan una paleta de roles de color;
/// los usuarios eligen el suyo desde un menú de selección.
/// </summary>
[SlashCommandGroup("colores", "Paleta de colores para los nombres de los usuarios")]
public sealed class ColorModule : SnowflakeModuleBase
{
    private readonly ColorService _color;
    private readonly MessagesService _msg;

    public ColorModule(ColorService color, MessagesService msg)
    {
        _color = color;
        _msg = msg;
    }

    [SlashCommand("instalar", "Crea los roles de la paleta de colores (admins)")]
    [SlashRequirePermissions(Permissions.ManageRoles)]
    [SlashRequireBotPermissions(Permissions.ManageRoles)]
    public async Task InstalarAsync(
        InteractionContext ctx,
        [Option("paleta", "Qué paleta instalar")]
        [Choice("Normal", "normal"), Choice("Pastel", "pastel")]
        string paleta = "normal")
    {
        // Crear 17 roles tarda más de 3 s: diferimos y luego editamos la respuesta.
        await ctx.DeferAsync();

        var tipo = paleta == "pastel" ? ColorService.PaletaType.Pastel : ColorService.PaletaType.Normal;
        var (creados, removidos, total) = await _color.InstalarAsync(ctx.Guild, tipo);

        var texto = creados == 0 && removidos == 0
            ? _msg.Get("Colores:InstalarRepetido", ("paleta", paleta))
            : _msg.Get("Colores:Instalar", ("paleta", paleta), ("total", total));

        await SafeEditAsync(ctx, texto);
    }

    [SlashCommand("desinstalar", "Elimina los roles de la paleta (admins)")]
    [SlashRequirePermissions(Permissions.ManageRoles)]
    [SlashRequireBotPermissions(Permissions.ManageRoles)]
    public async Task DesinstalarAsync(InteractionContext ctx)
    {
        await ctx.DeferAsync();

        var borrados = await _color.DesinstalarAsync(ctx.Guild);
        await SafeEditAsync(ctx, _msg.Get("Colores:Desinstalar", ("borrados", borrados)));
    }

    [SlashCommand("quitar", "Quítate el color que tienes puesto (para todos)")]
    public async Task QuitarAsync(InteractionContext ctx)
    {
        var miembro = await ctx.Guild.GetMemberAsync(ctx.User.Id);
        var tenia = await _color.QuitarAsync(miembro, ctx.Guild.Id);

        var texto = tenia ? _msg.Get("Colores:Quitado") : _msg.Get("Colores:NoTenia");
        await ResponderAsync(ctx, texto, ephemeral: true);
    }

    [SlashCommand("elegir", "Elige tu color entre los disponibles (para todos)")]
    public async Task ElegirAsync(InteractionContext ctx)
    {
        var selector = await _color.ConstruirSelectorAsync(ctx.Guild);
        if (selector is null)
        {
            await ResponderAsync(ctx, _msg.Get("Colores:NoInstalado"), ephemeral: true);
            return;
        }

        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .AddEmbed(selector.Value.Embed)
                .AddComponents(new[] { selector.Value.Select })
                .AsEphemeral());
    }

    [SlashCommand("listar", "Muestra los colores instalados")]
    public async Task ListarAsync(InteractionContext ctx)
    {
        var colores = await _color.ListarAsync(ctx.Guild.Id);

        var embed = new DiscordEmbedBuilder()
            .WithTitle(_msg.Get("Colores:ListarTitulo"))
            .WithColor(DiscordColor.Azure);

        if (colores.Count == 0)
        {
            embed.WithDescription(_msg.Get("Colores:ListarVacios"));
        }
        else
        {
            var lista = string.Join("  ", colores.Select(c => $"• {c.Name}"));
            embed.WithDescription(lista);
        }

        await ResponderAsync(ctx, embed, ephemeral: true);
    }
}
