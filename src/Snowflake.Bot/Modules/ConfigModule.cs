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
/// Comandos de configuración del bot por servidor.
/// </summary>
public sealed class ConfigModule : ApplicationCommandModule
{
    private readonly IDbContextFactory<BotDbContext> _dbFactory;
    private readonly MessagesService _msg;

    public ConfigModule(IDbContextFactory<BotDbContext> dbFactory, MessagesService msg)
    {
        _dbFactory = dbFactory;
        _msg = msg;
    }

    [SlashCommand("canal-logs", "Establece el canal donde se anuncian los incidentes de moderación")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task CanalLogsAsync(
        InteractionContext ctx,
        [Option("canal", "Canal de texto para los registros")] DiscordChannel canal)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var config = await db.GuildConfigs.FindAsync(ctx.Guild.Id);
        if (config is null)
        {
            config = new GuildConfig { GuildId = ctx.Guild.Id };
            db.GuildConfigs.Add(config);
        }

        config.ModLogChannelId = canal.Id;
        await db.SaveChangesAsync();

        var embed = new DiscordEmbedBuilder()
            .WithDescription(_msg.Get("Config:CanalLogsEstablecido", ("canal", canal.Mention)))
            .WithColor(DiscordColor.Green);

        await ctx.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AddEmbed(embed));
    }
}
