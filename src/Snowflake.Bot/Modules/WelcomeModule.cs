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
[SlashCommandGroup("bienvenida", "Configura los mensajes de bienvenida")]
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

    [SlashCommand("canal", "Establece el canal donde saludar a los nuevos miembros")]
    public async Task CanalAsync(
        InteractionContext ctx,
        [Option("canal", "Canal de texto para la bienvenida")] DiscordChannel canal)
    {
        await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.WelcomeChannelId = canal.Id);
        await ResponderAsync(ctx,
            _msg.Get("Bienvenida:ConfigCanalExito", ("canal", canal.Mention)), ephemeral: true);
    }

    [SlashCommand("mensaje", "Establece el mensaje de bienvenida (usa {usuario} y {servidor})")]
    public async Task MensajeAsync(
        InteractionContext ctx,
        [Option("mensaje", "Texto. Placeholders: {usuario} {servidor}. Máx 1900 caracteres.")]
        string mensaje)
    {
        if (mensaje.Length > 1900)
        {
            await ResponderAsync(ctx, _msg.Get("Bienvenida:MensajeLargo"), ephemeral: true);
            return;
        }

        await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.WelcomeMessage = mensaje);

        // Vista previa sustituyendo con quien ejecuta el comando.
        var vista = mensaje
            .Replace("{usuario}", ctx.User.Mention)
            .Replace("{servidor}", ctx.Guild.Name);

        await ResponderAsync(ctx,
            _msg.Get("Bienvenida:ConfigMensajeExito", ("vista", vista)), ephemeral: true);
    }

    [SlashCommand("ver", "Muestra la configuración actual de bienvenida")]
    public async Task VerAsync(InteractionContext ctx)
    {
        var config = await _settings.GetAsync(ctx.Guild.Id);

        var canal = config.WelcomeChannelId is ulong id
            ? $"<#{id}>"
            : _msg.Get("Bienvenida:VerNoConfigurado");

        var mensaje = string.IsNullOrWhiteSpace(config.WelcomeMessage)
            ? $"{_msg.Get("Bienvenida:MensajePorDefecto", ("usuario", ctx.User.Mention), ("servidor", ctx.Guild.Name))}\n{_msg.Get("Bienvenida:VerPorDefecto")}"
            : config.WelcomeMessage!;

        var embed = new DiscordEmbedBuilder()
            .WithTitle(_msg.Get("Bienvenida:VerTitulo"))
            .WithColor(DiscordColor.Azure)
            .AddField(_msg.Get("Bienvenida:VerCanal"), canal, true)
            .AddField(_msg.Get("Bienvenida:VerMensaje"), mensaje);

        await ResponderAsync(ctx, embed, ephemeral: true);
    }

    [SlashCommand("desactivar", "Desactiva la bienvenida")]
    public async Task DesactivarAsync(InteractionContext ctx)
    {
        var config = await _settings.GetAsync(ctx.Guild.Id);
        if (config.WelcomeChannelId is null)
        {
            await ResponderAsync(ctx, _msg.Get("Bienvenida:YaDesactivada"), ephemeral: true);
            return;
        }

        await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.WelcomeChannelId = null);
        await ResponderAsync(ctx, _msg.Get("Bienvenida:ConfigDesactivada"), ephemeral: true);
    }
}
