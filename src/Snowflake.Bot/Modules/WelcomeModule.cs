using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Microsoft.EntityFrameworkCore;
using Snowflake.Bot.Data;
using Snowflake.Bot.Data.Entities;
using Snowflake.Bot.Services;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Configuración del mensaje de bienvenida para nuevos miembros.
/// Comandos: /bienvenida canal | mensaje | ver | desactivar
/// </summary>
[SlashCommandGroup("bienvenida", "Configura los mensajes de bienvenida")]
[SlashRequirePermissions(Permissions.ManageGuild)]
public sealed class WelcomeModule : ApplicationCommandModule
{
    private readonly IDbContextFactory<BotDbContext> _dbFactory;
    private readonly MessagesService _msg;

    public WelcomeModule(IDbContextFactory<BotDbContext> dbFactory, MessagesService msg)
    {
        _dbFactory = dbFactory;
        _msg = msg;
    }

    [SlashCommand("canal", "Establece el canal donde saludar a los nuevos miembros")]
    public async Task CanalAsync(
        InteractionContext ctx,
        [Option("canal", "Canal de texto para la bienvenida")] DiscordChannel canal)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var config = await db.GuildConfigs.FindAsync(ctx.Guild.Id);
        if (config is null)
        {
            config = new GuildConfig { GuildId = ctx.Guild.Id };
            db.GuildConfigs.Add(config);
        }

        config.WelcomeChannelId = canal.Id;
        await db.SaveChangesAsync();

        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent(_msg.Get("Bienvenida:ConfigCanalExito", ("canal", canal.Mention)))
                .AsEphemeral());
    }

    [SlashCommand("mensaje", "Establece el mensaje de bienvenida (usa {usuario} y {servidor})")]
    public async Task MensajeAsync(
        InteractionContext ctx,
        [Option("mensaje", "Texto. Placeholders: {usuario} {servidor}. Máx 1900 caracteres.")]
        string mensaje)
    {
        if (mensaje.Length > 1900)
        {
            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(_msg.Get("Bienvenida:MensajeLargo"))
                    .AsEphemeral());
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();

        var config = await db.GuildConfigs.FindAsync(ctx.Guild.Id);
        if (config is null)
        {
            config = new GuildConfig { GuildId = ctx.Guild.Id };
            db.GuildConfigs.Add(config);
        }

        config.WelcomeMessage = mensaje;
        await db.SaveChangesAsync();

        // Vista previa sustituyendo con quien ejecuta el comando.
        var vista = mensaje
            .Replace("{usuario}", ctx.User.Mention)
            .Replace("{servidor}", ctx.Guild.Name);

        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent(_msg.Get("Bienvenida:ConfigMensajeExito", ("vista", vista)))
                .AsEphemeral());
    }

    [SlashCommand("ver", "Muestra la configuración actual de bienvenida")]
    public async Task VerAsync(InteractionContext ctx)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var config = await db.GuildConfigs.FindAsync(ctx.Guild.Id);

        var canal = config?.WelcomeChannelId is ulong id
            ? $"<#{id}>"
            : _msg.Get("Bienvenida:VerNoConfigurado");

        var mensaje = string.IsNullOrWhiteSpace(config?.WelcomeMessage)
            ? $"{_msg.Get("Bienvenida:MensajePorDefecto", ("usuario", ctx.User.Mention), ("servidor", ctx.Guild.Name))}\n{_msg.Get("Bienvenida:VerPorDefecto")}"
            : config!.WelcomeMessage!;

        var embed = new DiscordEmbedBuilder()
            .WithTitle(_msg.Get("Bienvenida:VerTitulo"))
            .WithColor(DiscordColor.Azure)
            .AddField(_msg.Get("Bienvenida:VerCanal"), canal, true)
            .AddField(_msg.Get("Bienvenida:VerMensaje"), mensaje);

        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(embed).AsEphemeral());
    }

    [SlashCommand("desactivar", "Desactiva la bienvenida")]
    public async Task DesactivarAsync(InteractionContext ctx)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var config = await db.GuildConfigs.FindAsync(ctx.Guild.Id);

        if (config is null || config.WelcomeChannelId is null)
        {
            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(_msg.Get("Bienvenida:YaDesactivada"))
                    .AsEphemeral());
            return;
        }

        config.WelcomeChannelId = null;
        await db.SaveChangesAsync();

        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent(_msg.Get("Bienvenida:ConfigDesactivada"))
                .AsEphemeral());
    }
}