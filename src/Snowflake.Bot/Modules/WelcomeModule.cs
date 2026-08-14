using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Snowflake.Bot.Services;
using Snowflake.Bot.Services.Settings;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Configuración del mensaje de bienvenida para nuevos miembros.
/// Comandos: /bienvenida canal | mensaje | ver | desactivar
/// </summary>
[SlashCommandGroup("welcome", "Configure welcome messages")]
[NameLocalization(Localization.Spanish, "bienvenida")]
[NameLocalization(Localization.Portuguese, "boasvindas")]
[DescriptionLocalization(Localization.Spanish, "Configura los mensajes de bienvenida")]
[DescriptionLocalization(Localization.Portuguese, "Configura as mensagens de boas-vindas")]
[SlashRequirePermissions(Permissions.ManageGuild)]
public sealed class WelcomeModule : SnowflakeModuleBase
{
    private readonly GuildSettingsService _settings;
    private readonly MessagesService _msg;

    public WelcomeModule(GuildSettingsService settings, MessagesService msg)
    {
        _settings = settings;
        _msg = msg;
    }

    [SlashCommand("channel", "Set the channel where new members are greeted")]
    [NameLocalization(Localization.Spanish, "canal")]
    [NameLocalization(Localization.Portuguese, "canal")]
    [DescriptionLocalization(Localization.Spanish, "Establece el canal donde saludar a los nuevos miembros")]
    [DescriptionLocalization(Localization.Portuguese, "Define o canal para saudar os novos membros")]
    public async Task CanalAsync(
        InteractionContext ctx,
        [Option("channel", "Text channel for welcomes")]
        [NameLocalization(Localization.Spanish, "canal")]
        [NameLocalization(Localization.Portuguese, "canal")]
        [DescriptionLocalization(Localization.Spanish, "Canal de texto para la bienvenida")]
        [DescriptionLocalization(Localization.Portuguese, "Canal de texto para as boas-vindas")] DiscordChannel canal)
    {
        await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.WelcomeChannelId = canal.Id);
        await ResponderAsync(ctx,
            _msg.Get(ctx.Guild.Id, "Bienvenida:ConfigCanalExito", ("canal", canal.Mention)), ephemeral: true);
    }

    [SlashCommand("message", "Set the welcome message (use {usuario} and {servidor})")]
    [NameLocalization(Localization.Spanish, "mensaje")]
    [NameLocalization(Localization.Portuguese, "mensagem")]
    [DescriptionLocalization(Localization.Spanish, "Establece el mensaje de bienvenida (usa {usuario} y {servidor})")]
    [DescriptionLocalization(Localization.Portuguese, "Define a mensagem de boas-vindas (use {usuario} e {servidor})")]
    public async Task MensajeAsync(
        InteractionContext ctx,
        [Option("message", "Text. Placeholders: {usuario} {servidor}. Max 1900 characters.")]
        [NameLocalization(Localization.Spanish, "mensaje")]
        [NameLocalization(Localization.Portuguese, "mensagem")]
        [DescriptionLocalization(Localization.Spanish, "Texto. Placeholders: {usuario} {servidor}. Máx 1900 caracteres.")]
        [DescriptionLocalization(Localization.Portuguese, "Texto. Placeholders: {usuario} {servidor}. Máx. 1900 caracteres.")]
        string mensaje)
    {
        if (mensaje.Length > 1900)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Bienvenida:MensajeLargo"), ephemeral: true);
            return;
        }

        await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.WelcomeMessage = mensaje);

        // Vista previa sustituyendo con quien ejecuta el comando.
        var vista = mensaje
            .Replace("{usuario}", ctx.User.Mention)
            .Replace("{servidor}", ctx.Guild.Name);

        await ResponderAsync(ctx,
            _msg.Get(ctx.Guild.Id, "Bienvenida:ConfigMensajeExito", ("vista", vista)), ephemeral: true);
    }

    [SlashCommand("show", "Show the current welcome settings")]
    [NameLocalization(Localization.Spanish, "ver")]
    [NameLocalization(Localization.Portuguese, "ver")]
    [DescriptionLocalization(Localization.Spanish, "Muestra la configuración actual de bienvenida")]
    [DescriptionLocalization(Localization.Portuguese, "Mostra a configuração atual de boas-vindas")]
    public async Task VerAsync(InteractionContext ctx)
    {
        var config = await _settings.GetAsync(ctx.Guild.Id);

        var canal = config.WelcomeChannelId is ulong id
            ? $"<#{id}>"
            : _msg.Get(ctx.Guild.Id, "Bienvenida:VerNoConfigurado");

        var mensaje = string.IsNullOrWhiteSpace(config.WelcomeMessage)
            ? $"{_msg.Get(ctx.Guild.Id, "Bienvenida:MensajePorDefecto", ("usuario", ctx.User.Mention), ("servidor", ctx.Guild.Name))}\n{_msg.Get(ctx.Guild.Id, "Bienvenida:VerPorDefecto")}"
            : config.WelcomeMessage!;

        var embed = new DiscordEmbedBuilder()
            .WithTitle(_msg.Get(ctx.Guild.Id, "Bienvenida:VerTitulo"))
            .WithColor(DiscordColor.Azure)
            .AddField(_msg.Get(ctx.Guild.Id, "Bienvenida:VerCanal"), canal, true)
            .AddField(_msg.Get(ctx.Guild.Id, "Bienvenida:VerMensaje"), mensaje);

        await ResponderAsync(ctx, embed, ephemeral: true);
    }

    [SlashCommand("disable", "Disable welcome messages")]
    [NameLocalization(Localization.Spanish, "desactivar")]
    [NameLocalization(Localization.Portuguese, "desativar")]
    [DescriptionLocalization(Localization.Spanish, "Desactiva la bienvenida")]
    [DescriptionLocalization(Localization.Portuguese, "Desativa as boas-vindas")]
    public async Task DesactivarAsync(InteractionContext ctx)
    {
        var config = await _settings.GetAsync(ctx.Guild.Id);
        if (config.WelcomeChannelId is null)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Bienvenida:YaDesactivada"), ephemeral: true);
            return;
        }

        await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.WelcomeChannelId = null);
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Bienvenida:ConfigDesactivada"), ephemeral: true);
    }
}
