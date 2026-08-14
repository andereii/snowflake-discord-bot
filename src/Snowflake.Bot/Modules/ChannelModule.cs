using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Snowflake.Bot.Services;
using Snowflake.Bot.Services.Settings;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Creación de canales bajo demanda y configuración del sistema join-to-create.
/// Comandos: /canal crear · /canal hub · /canal hub-quitar · /canal plantilla
/// </summary>
[SlashCommandGroup("channel", "Create channels and configure join-to-create")]
[NameLocalization(Localization.Spanish, "canal")]
[NameLocalization(Localization.Portuguese, "canal")]
[DescriptionLocalization(Localization.Spanish, "Crea canales y configura el join-to-create")]
[DescriptionLocalization(Localization.Portuguese, "Cria canais e configura o join-to-create")]
public sealed class ChannelModule : SnowflakeModuleBase
{
    private readonly GuildSettingsService _settings;
    private readonly MessagesService _msg;

    public ChannelModule(GuildSettingsService settings, MessagesService msg)
    {
        _settings = settings;
        _msg = msg;
    }

    [SlashCommand("create", "Create a text or voice channel on demand")]
    [NameLocalization(Localization.Spanish, "crear")]
    [NameLocalization(Localization.Portuguese, "criar")]
    [DescriptionLocalization(Localization.Spanish, "Crea un canal de texto o voz bajo demanda")]
    [DescriptionLocalization(Localization.Portuguese, "Cria um canal de texto ou voz sob demanda")]
    [SlashRequirePermissions(Permissions.ManageChannels)]
    [SlashRequireBotPermissions(Permissions.ManageChannels)]
    public async Task CrearAsync(
        InteractionContext ctx,
        [Option("name", "Channel name")]
        [NameLocalization(Localization.Spanish, "nombre")]
        [NameLocalization(Localization.Portuguese, "nome")]
        [DescriptionLocalization(Localization.Spanish, "Nombre del canal")]
        [DescriptionLocalization(Localization.Portuguese, "Nome do canal")] string nombre,
        [Option("type", "Voice or text")]
        [NameLocalization(Localization.Spanish, "tipo")]
        [NameLocalization(Localization.Portuguese, "tipo")]
        [DescriptionLocalization(Localization.Spanish, "Voz o texto")]
        [DescriptionLocalization(Localization.Portuguese, "Voz ou texto")]
        [Choice("Voice", "voice"), Choice("Text", "text")] string tipo,
        [Option("category", "Category to create it in (optional)")]
        [NameLocalization(Localization.Spanish, "categoria")]
        [NameLocalization(Localization.Portuguese, "categoria")]
        [DescriptionLocalization(Localization.Spanish, "Categoría donde crearlo (opcional)")]
        [DescriptionLocalization(Localization.Portuguese, "Categoria onde criá-lo (opcional)")] DiscordChannel? categoria = null)
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

        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Voces:Creado", ("canal", canal.Mention)));
    }

    [SlashCommand("hub", "Set the join-to-create hub voice channel")]
    [NameLocalization(Localization.Spanish, "hub")]
    [NameLocalization(Localization.Portuguese, "hub")]
    [DescriptionLocalization(Localization.Spanish, "Establece el canal de voz 'hub' del join-to-create")]
    [DescriptionLocalization(Localization.Portuguese, "Define o canal de voz 'hub' do join-to-create")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task HubAsync(
        InteractionContext ctx,
        [Option("channel", "Voice channel to act as the hub")]
        [NameLocalization(Localization.Spanish, "canal")]
        [NameLocalization(Localization.Portuguese, "canal")]
        [DescriptionLocalization(Localization.Spanish, "Canal de voz que hará de hub")]
        [DescriptionLocalization(Localization.Portuguese, "Canal de voz que será o hub")] DiscordChannel canal)
    {
        if (canal.Type != ChannelType.Voice)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Voces:HubDebeSerVoz"), ephemeral: true);
            return;
        }

        await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.HubChannelId = canal.Id);
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Voces:HubEstablecido", ("canal", canal.Mention)));
    }

    [SlashCommand("hub-remove", "Disable join-to-create")]
    [NameLocalization(Localization.Spanish, "hub-quitar")]
    [NameLocalization(Localization.Portuguese, "hub-remover")]
    [DescriptionLocalization(Localization.Spanish, "Desactiva el join-to-create")]
    [DescriptionLocalization(Localization.Portuguese, "Desativa o join-to-create")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task HubQuitarAsync(InteractionContext ctx)
    {
        var config = await _settings.GetAsync(ctx.Guild.Id);
        if (config.HubChannelId is null)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Voces:HubQuitado"), ephemeral: true);
            return;
        }

        await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.HubChannelId = null);
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Voces:HubQuitado"));
    }

    [SlashCommand("template", "Customize temporary channel names ({usuario} placeholder; empty = default)")]
    [NameLocalization(Localization.Spanish, "plantilla")]
    [NameLocalization(Localization.Portuguese, "modelo")]
    [DescriptionLocalization(Localization.Spanish, "Personaliza el nombre de los canales temporales (placeholder {usuario}; vacío = por defecto)")]
    [DescriptionLocalization(Localization.Portuguese, "Personaliza o nome dos canais temporários (placeholder {usuario}; vazio = padrão)")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task PlantillaAsync(
        InteractionContext ctx,
        [Option("template", "Name template, e.g. '🔊 {usuario}'. Empty = reset.")]
        [NameLocalization(Localization.Spanish, "plantilla")]
        [NameLocalization(Localization.Portuguese, "modelo")]
        [DescriptionLocalization(Localization.Spanish, "Plantilla de nombre, p. ej. '🔊 {usuario}'. Vacío = restablecer.")]
        [DescriptionLocalization(Localization.Portuguese, "Modelo de nome, ex. '🔊 {usuario}'. Vazio = redefinir.")]
        string? plantilla = null)
    {
        if (string.IsNullOrWhiteSpace(plantilla))
        {
            await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.TempChannelNameTemplate = null);
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Voces:PlantillaBorrada"));
            return;
        }
        if (plantilla.Length > 100)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Voces:PlantillaLarga"), ephemeral: true);
            return;
        }

        await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.TempChannelNameTemplate = plantilla);
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Voces:PlantillaEstablecida",
            ("vista", plantilla.Replace("{usuario}", ctx.User.Username))));
    }
}
