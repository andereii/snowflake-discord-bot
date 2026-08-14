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

    [SlashCommand("log-channel", "Set the channel where moderation incidents are announced")]
    [NameLocalization(Localization.Spanish, "canal-logs")]
    [NameLocalization(Localization.Portuguese, "canal-de-logs")]
    [DescriptionLocalization(Localization.Spanish, "Establece el canal donde se anuncian los incidentes de moderación")]
    [DescriptionLocalization(Localization.Portuguese, "Define o canal onde os incidentes de moderação são anunciados")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task CanalLogsAsync(
        InteractionContext ctx,
        [Option("channel", "Text channel for the logs")]
        [NameLocalization(Localization.Spanish, "canal")]
        [NameLocalization(Localization.Portuguese, "canal")]
        [DescriptionLocalization(Localization.Spanish, "Canal de texto para los registros")]
        [DescriptionLocalization(Localization.Portuguese, "Canal de texto para os registros")] DiscordChannel canal)
    {
        await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.ModLogChannelId = canal.Id);

        var embed = new DiscordEmbedBuilder()
            .WithDescription(_msg.Get(ctx.Guild.Id, "Config:CanalLogsEstablecido", ("canal", canal.Mention)))
            .WithColor(DiscordColor.Green);

        await ResponderAsync(ctx, embed);
    }

    [SlashCommand("show", "Show the summary of all bot settings on this server")]
    [NameLocalization(Localization.Spanish, "ver")]
    [NameLocalization(Localization.Portuguese, "ver")]
    [DescriptionLocalization(Localization.Spanish, "Muestra el resumen de todos los ajustes del bot en este servidor")]
    [DescriptionLocalization(Localization.Portuguese, "Mostra o resumo de todos os ajustes do bot neste servidor")]
    public async Task VerAsync(InteractionContext ctx)
    {
        var s = await _settings.GetSnapshotAsync(ctx.Guild.Id);

        var embed = new DiscordEmbedBuilder()
            .WithTitle(_msg.Get(ctx.Guild.Id, "Config:VerTitulo", ("servidor", ctx.Guild.Name)))
            .WithColor(DiscordColor.Azure);

        embed.AddField(_msg.Get(ctx.Guild.Id, "Config:VerModeracion"),
            s.Moderation.LogChannelId is { } log
                ? $"<#{log}>"
                : _msg.Get(ctx.Guild.Id, "Config:VerNoConfigurado"), true);

        embed.AddField(_msg.Get(ctx.Guild.Id, "Config:VerBienvenida"),
            s.Welcome.Enabled
                ? $"<#{s.Welcome.ChannelId}>"
                : _msg.Get(ctx.Guild.Id, "Config:VerDesactivado"), true);

        embed.AddField(_msg.Get(ctx.Guild.Id, "Config:VerVoces"),
            s.Voice.HubChannelId is { } hub
                ? $"<#{hub}>"
                : _msg.Get(ctx.Guild.Id, "Config:VerDesactivado"), true);

        var musica = s.Music.DjRoleId is { } dj
            ? _msg.Get(ctx.Guild.Id, "Config:VerDj", ("rol", $"<@&{dj}>"))
            : _msg.Get(ctx.Guild.Id, "Config:VerSinDj");
        embed.AddField(_msg.Get(ctx.Guild.Id, "Config:VerMusica"), musica, true);

        embed.AddField(_msg.Get(ctx.Guild.Id, "Config:VerAi"),
            _msg.Get(ctx.Guild.Id, "Config:VerAiDetalle",
                ("chat", SiNo(s.Ai.ChatEnabled)),
                ("menciones", SiNo(s.Ai.MentionsEnabled)),
                ("espontaneo", SiNo(s.Ai.SpontaneousEnabled))), true);

        embed.AddField(_msg.Get(ctx.Guild.Id, "Config:VerDescargas"), SiNo(s.Downloads.Enabled), true);

        embed.AddField(_msg.Get(ctx.Guild.Id, "Config:VerBloqueados"),
            s.BlockedChannels.Count > 0
                ? string.Join("\n", s.BlockedChannels.Select(id => $"<#{id}>"))
                : _msg.Get(ctx.Guild.Id, "Config:VerBloqueadosVacio"), true);

        embed.AddField(_msg.Get(ctx.Guild.Id, "Config:VerConteo"),
            s.Counting is { } c
                ? (c.Enabled
                    ? $"{_msg.Get(ctx.Guild.Id, "Config:VerCanalActivo", ("canal", $"<#{c.ChannelId}>"))} · {_msg.Get(ctx.Guild.Id, "Config:VerBase", ("base", c.Base))}"
                    : _msg.Get(ctx.Guild.Id, "Config:VerDesactivado"))
                : _msg.Get(ctx.Guild.Id, "Config:VerNuncaConfigurado"), true);

        embed.AddField(_msg.Get(ctx.Guild.Id, "Config:VerYoutube"),
            s.YouTube is { } yt
                ? $"{yt.ChannelName} → <#{yt.NotifyChannelId}>"
                : _msg.Get(ctx.Guild.Id, "Config:VerDesactivado"), true);

        embed.AddField(_msg.Get(ctx.Guild.Id, "Config:VerIdioma"), NombreIdioma(s.Language), true);

        embed.WithFooter(_msg.Get(ctx.Guild.Id, "Config:VerPie"));
        await ResponderAsync(ctx, embed, ephemeral: true);
    }

    /// <summary>Cambia el idioma de los mensajes del bot en este servidor (en/es/pt).</summary>
    [SlashCommand("lang", "Change the bot's language on this server")]
    [NameLocalization(Localization.Spanish, "idioma")]
    [NameLocalization(Localization.Portuguese, "idioma")]
    [DescriptionLocalization(Localization.Spanish, "Cambia el idioma del bot en este servidor")]
    [DescriptionLocalization(Localization.Portuguese, "Muda o idioma do bot neste servidor")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task LangAsync(
        InteractionContext ctx,
        [Option("language", "Bot language (empty = show current)")]
        [NameLocalization(Localization.Spanish, "idioma")]
        [NameLocalization(Localization.Portuguese, "idioma")]
        [DescriptionLocalization(Localization.Spanish, "Idioma del bot (vacío = mostrar el actual)")]
        [DescriptionLocalization(Localization.Portuguese, "Idioma do bot (vazio = mostrar o atual)")]
        [Choice("English", "en"), Choice("Español", "es"), Choice("Português", "pt")]
        string? idioma = null)
    {
        if (idioma is null)
        {
            var actual = (await _settings.GetAsync(ctx.Guild.Id)).Language;
            await ResponderAsync(ctx,
                _msg.Get(ctx.Guild.Id, "Config:VerIdioma") + ": " + NombreIdioma(actual), ephemeral: true);
            return;
        }

        var idiomaNormalizado = Languages.Normalizar(idioma);
        await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.Language = idiomaNormalizado);

        // Respondemos en el idioma recién elegido.
        await ResponderAsync(ctx, _msg.Get(idiomaNormalizado, "Config:IdiomaCambiado",
            ("idioma", NombreIdioma(idiomaNormalizado))));
    }

    private static string NombreIdioma(string lang) => lang switch
    {
        Languages.Spanish => "Español",
        Languages.Portuguese => "Português",
        _ => "English"
    };

    private static string SiNo(bool valor) =>
        valor ? BotEmojis.Check : BotEmojis.Error;
}
