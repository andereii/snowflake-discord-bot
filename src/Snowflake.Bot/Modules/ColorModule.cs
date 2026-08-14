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
[SlashCommandGroup("colors", "Color palette for user names")]
[NameLocalization(Localization.Spanish, "colores")]
[NameLocalization(Localization.Portuguese, "cores")]
[DescriptionLocalization(Localization.Spanish, "Paleta de colores para los nombres de los usuarios")]
[DescriptionLocalization(Localization.Portuguese, "Paleta de cores para os nomes dos usuários")]
public sealed class ColorModule : SnowflakeModuleBase
{
    private readonly ColorService _color;
    private readonly MessagesService _msg;

    public ColorModule(ColorService color, MessagesService msg)
    {
        _color = color;
        _msg = msg;
    }

    [SlashCommand("install", "Create the color palette roles (admins)")]
    [NameLocalization(Localization.Spanish, "instalar")]
    [NameLocalization(Localization.Portuguese, "instalar")]
    [DescriptionLocalization(Localization.Spanish, "Crea los roles de la paleta de colores (admins)")]
    [DescriptionLocalization(Localization.Portuguese, "Cria os cargos da paleta de cores (admins)")]
    [SlashRequirePermissions(Permissions.ManageRoles)]
    [SlashRequireBotPermissions(Permissions.ManageRoles)]
    public async Task InstalarAsync(
        InteractionContext ctx,
        [Option("palette", "Which palette to install")]
        [NameLocalization(Localization.Spanish, "paleta")]
        [NameLocalization(Localization.Portuguese, "paleta")]
        [DescriptionLocalization(Localization.Spanish, "Qué paleta instalar")]
        [DescriptionLocalization(Localization.Portuguese, "Qual paleta instalar")]
        [Choice("Normal", "normal"), Choice("Pastel", "pastel")]
        string paleta = "normal")
    {
        // Crear 17 roles tarda más de 3 s: diferimos y luego editamos la respuesta.
        await ctx.DeferAsync();

        var tipo = paleta == "pastel" ? ColorService.PaletaType.Pastel : ColorService.PaletaType.Normal;
        var (creados, removidos, total) = await _color.InstalarAsync(ctx.Guild, tipo);

        var texto = creados == 0 && removidos == 0
            ? _msg.Get(ctx.Guild.Id, "Colores:InstalarRepetido", ("paleta", paleta))
            : _msg.Get(ctx.Guild.Id, "Colores:Instalar", ("paleta", paleta), ("total", total));

        await SafeEditAsync(ctx, texto);
    }

    [SlashCommand("uninstall", "Remove the palette roles (admins)")]
    [NameLocalization(Localization.Spanish, "desinstalar")]
    [NameLocalization(Localization.Portuguese, "desinstalar")]
    [DescriptionLocalization(Localization.Spanish, "Elimina los roles de la paleta (admins)")]
    [DescriptionLocalization(Localization.Portuguese, "Remove os cargos da paleta (admins)")]
    [SlashRequirePermissions(Permissions.ManageRoles)]
    [SlashRequireBotPermissions(Permissions.ManageRoles)]
    public async Task DesinstalarAsync(InteractionContext ctx)
    {
        await ctx.DeferAsync();

        var borrados = await _color.DesinstalarAsync(ctx.Guild);
        await SafeEditAsync(ctx, _msg.Get(ctx.Guild.Id, "Colores:Desinstalar", ("borrados", borrados)));
    }

    [SlashCommand("remove", "Remove the color you currently have (for everyone)")]
    [NameLocalization(Localization.Spanish, "quitar")]
    [NameLocalization(Localization.Portuguese, "remover")]
    [DescriptionLocalization(Localization.Spanish, "Quítate el color que tienes puesto (para todos)")]
    [DescriptionLocalization(Localization.Portuguese, "Remove a cor que você tem agora (para todos)")]
    public async Task QuitarAsync(InteractionContext ctx)
    {
        var miembro = await ctx.Guild.GetMemberAsync(ctx.User.Id);
        var tenia = await _color.QuitarAsync(miembro, ctx.Guild.Id);

        var texto = tenia ? _msg.Get(ctx.Guild.Id, "Colores:Quitado") : _msg.Get(ctx.Guild.Id, "Colores:NoTenia");
        await ResponderAsync(ctx, texto, ephemeral: true);
    }

    [SlashCommand("choose", "Choose your color from the available ones (for everyone)")]
    [NameLocalization(Localization.Spanish, "elegir")]
    [NameLocalization(Localization.Portuguese, "escolher")]
    [DescriptionLocalization(Localization.Spanish, "Elige tu color entre los disponibles (para todos)")]
    [DescriptionLocalization(Localization.Portuguese, "Escolha sua cor entre as disponíveis (para todos)")]
    public async Task ElegirAsync(InteractionContext ctx)
    {
        var selector = await _color.ConstruirSelectorAsync(ctx.Guild);
        if (selector is null)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Colores:NoInstalado"), ephemeral: true);
            return;
        }

        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .AddEmbed(selector.Value.Embed)
                .AddComponents(new[] { selector.Value.Select })
                .AsEphemeral());
    }

    [SlashCommand("list", "Show the installed colors")]
    [NameLocalization(Localization.Spanish, "listar")]
    [NameLocalization(Localization.Portuguese, "listar")]
    [DescriptionLocalization(Localization.Spanish, "Muestra los colores instalados")]
    [DescriptionLocalization(Localization.Portuguese, "Mostra as cores instaladas")]
    public async Task ListarAsync(InteractionContext ctx)
    {
        var colores = await _color.ListarAsync(ctx.Guild.Id);

        var embed = new DiscordEmbedBuilder()
            .WithTitle(_msg.Get(ctx.Guild.Id, "Colores:ListarTitulo"))
            .WithColor(DiscordColor.Azure);

        if (colores.Count == 0)
        {
            embed.WithDescription(_msg.Get(ctx.Guild.Id, "Colores:ListarVacios"));
        }
        else
        {
            var lista = string.Join("  ", colores.Select(c => $"• {c.Name}"));
            embed.WithDescription(lista);
        }

        await ResponderAsync(ctx, embed, ephemeral: true);
    }
}
