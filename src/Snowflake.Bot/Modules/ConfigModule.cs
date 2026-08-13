using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Snowflake.Bot.Services;
using Snowflake.Bot.Services.Settings;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Configuración general del bot por servidor: canal de logs de moderación y
/// resumen completo de ajustes (el equivalente en Discord al panel web).
/// </summary>
public sealed class ConfigModule : SnowflakeModuleBase
{
    private readonly GuildSettingsService _settings;
    private readonly MessagesService _msg;

    public ConfigModule(GuildSettingsService settings, MessagesService msg)
    {
        _settings = settings;
        _msg = msg;
    }

    [SlashCommand("canal-logs", "Establece el canal donde se anuncian los incidentes de moderación")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task CanalLogsAsync(
        InteractionContext ctx,
        [Option("canal", "Canal de texto para los registros")] DiscordChannel canal)
    {
        await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.ModLogChannelId = canal.Id);

        var embed = new DiscordEmbedBuilder()
            .WithDescription(_msg.Get("Config:CanalLogsEstablecido", ("canal", canal.Mention)))
            .WithColor(DiscordColor.Green);

        await ResponderAsync(ctx, embed);
    }

    [SlashCommand("ver", "Muestra el resumen de todos los ajustes del bot en este servidor")]
    public async Task VerAsync(InteractionContext ctx)
    {
        var s = await _settings.GetSnapshotAsync(ctx.Guild.Id);

        var embed = new DiscordEmbedBuilder()
            .WithTitle(_msg.Get("Config:VerTitulo", ("servidor", ctx.Guild.Name)))
            .WithColor(DiscordColor.Azure);

        embed.AddField(_msg.Get("Config:VerModeracion"),
            s.Moderation.LogChannelId is { } log
                ? $"<#{log}>"
                : _msg.Get("Config:VerNoConfigurado"), true);

        embed.AddField(_msg.Get("Config:VerBienvenida"),
            s.Welcome.Enabled
                ? $"<#{s.Welcome.ChannelId}>"
                : _msg.Get("Config:VerDesactivado"), true);

        embed.AddField(_msg.Get("Config:VerVoces"),
            s.Voice.HubChannelId is { } hub
                ? $"<#{hub}>"
                : _msg.Get("Config:VerDesactivado"), true);

        var musica = s.Music.DjRoleId is { } dj
            ? _msg.Get("Config:VerDj", ("rol", $"<@&{dj}>"))
            : _msg.Get("Config:VerSinDj");
        embed.AddField(_msg.Get("Config:VerMusica"), musica, true);

        embed.AddField(_msg.Get("Config:VerAi"),
            _msg.Get("Config:VerAiDetalle",
                ("chat", SiNo(s.Ai.ChatEnabled)),
                ("menciones", SiNo(s.Ai.MentionsEnabled)),
                ("espontaneo", SiNo(s.Ai.SpontaneousEnabled))), true);

        embed.AddField(_msg.Get("Config:VerDescargas"), SiNo(s.Downloads.Enabled), true);

        embed.AddField(_msg.Get("Config:VerBloqueados"),
            s.BlockedChannels.Count > 0
                ? string.Join("\n", s.BlockedChannels.Select(id => $"<#{id}>"))
                : _msg.Get("Config:VerBloqueadosVacio"), true);

        embed.AddField(_msg.Get("Config:VerConteo"),
            s.Counting is { } c
                ? (c.Enabled
                    ? $"{_msg.Get("Config:VerCanalActivo", ("canal", $"<#{c.ChannelId}>"))} · {_msg.Get("Config:VerBase", ("base", c.Base))}"
                    : _msg.Get("Config:VerDesactivado"))
                : _msg.Get("Config:VerNuncaConfigurado"), true);

        embed.AddField(_msg.Get("Config:VerYoutube"),
            s.YouTube is { } yt
                ? $"{yt.ChannelName} → <#{yt.NotifyChannelId}>"
                : _msg.Get("Config:VerDesactivado"), true);

        embed.WithFooter(_msg.Get("Config:VerPie"));
        await ResponderAsync(ctx, embed, ephemeral: true);
    }

    private static string SiNo(bool valor) =>
        valor ? BotEmojis.Check : BotEmojis.Error;
}
