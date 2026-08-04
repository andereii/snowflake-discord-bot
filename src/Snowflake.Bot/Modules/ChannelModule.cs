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
/// Creación de canales bajo demanda y configuración del sistema join-to-create.
/// Comandos: /canal crear · /canal hub · /canal hub-quitar
/// </summary>
[SlashCommandGroup("canal", "Crea canales y configura el join-to-create")]
public sealed class ChannelModule : ApplicationCommandModule
{
    private readonly IDbContextFactory<BotDbContext> _dbFactory;
    private readonly MessagesService _msg;

    public ChannelModule(IDbContextFactory<BotDbContext> dbFactory, MessagesService msg)
    {
        _dbFactory = dbFactory;
        _msg = msg;
    }

    [SlashCommand("crear", "Crea un canal de texto o voz bajo demanda")]
    [SlashRequirePermissions(Permissions.ManageChannels)]
    [SlashRequireBotPermissions(Permissions.ManageChannels)]
    public async Task CrearAsync(
        InteractionContext ctx,
        [Option("nombre", "Nombre del canal")] string nombre,
        [Option("tipo", "Voz o texto")]
        [Choice("Voz", "voice"), Choice("Texto", "text")] string tipo,
        [Option("categoria", "Categoría donde crearlo (opcional)")] DiscordChannel? categoria = null)
    {
        var parent = categoria is not null && categoria.Type == ChannelType.Category
            ? categoria
            : null;

        DiscordChannel canal;
        if (tipo == "voice")
        {
            canal = parent is null
                ? await ctx.Guild.CreateVoiceChannelAsync(nombre, reason: "Creado con /canal crear")
                : await ctx.Guild.CreateVoiceChannelAsync(nombre, parent, reason: "Creado con /canal crear");
        }
        else
        {
            canal = parent is null
                ? await ctx.Guild.CreateTextChannelAsync(nombre, reason: "Creado con /canal crear")
                : await ctx.Guild.CreateTextChannelAsync(nombre, parent, reason: "Creado con /canal crear");
        }

        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent(_msg.Get("Voces:Creado", ("canal", canal.Mention))));
    }

    [SlashCommand("hub", "Establece el canal de voz 'hub' del join-to-create")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task HubAsync(
        InteractionContext ctx,
        [Option("canal", "Canal de voz que hará de hub")] DiscordChannel canal)
    {
        if (canal.Type != ChannelType.Voice)
        {
            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(_msg.Get("Voces:HubDebeSerVoz"))
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
        config.HubChannelId = canal.Id;
        await db.SaveChangesAsync();

        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent(_msg.Get("Voces:HubEstablecido", ("canal", canal.Mention))));
    }

    [SlashCommand("hub-quitar", "Desactiva el join-to-create")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task HubQuitarAsync(InteractionContext ctx)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var config = await db.GuildConfigs.FindAsync(ctx.Guild.Id);

        if (config is null || config.HubChannelId is null)
        {
            await ctx.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent(_msg.Get("Voces:HubQuitado"))
                    .AsEphemeral());
            return;
        }

        config.HubChannelId = null;
        await db.SaveChangesAsync();

        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent(_msg.Get("Voces:HubQuitado")));
    }
}