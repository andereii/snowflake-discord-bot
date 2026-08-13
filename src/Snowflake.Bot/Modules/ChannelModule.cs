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
[SlashCommandGroup("canal", "Crea canales y configura el join-to-create")]
public sealed class ChannelModule : SnowflakeModuleBase
{
    private readonly GuildSettingsService _settings;
    private readonly MessagesService _msg;

    public ChannelModule(GuildSettingsService settings, MessagesService msg)
    {
        _settings = settings;
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

        await ResponderAsync(ctx, _msg.Get("Voces:Creado", ("canal", canal.Mention)));
    }

    [SlashCommand("hub", "Establece el canal de voz 'hub' del join-to-create")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task HubAsync(
        InteractionContext ctx,
        [Option("canal", "Canal de voz que hará de hub")] DiscordChannel canal)
    {
        if (canal.Type != ChannelType.Voice)
        {
            await ResponderAsync(ctx, _msg.Get("Voces:HubDebeSerVoz"), ephemeral: true);
            return;
        }

        await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.HubChannelId = canal.Id);
        await ResponderAsync(ctx, _msg.Get("Voces:HubEstablecido", ("canal", canal.Mention)));
    }

    [SlashCommand("hub-quitar", "Desactiva el join-to-create")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task HubQuitarAsync(InteractionContext ctx)
    {
        var config = await _settings.GetAsync(ctx.Guild.Id);
        if (config.HubChannelId is null)
        {
            await ResponderAsync(ctx, _msg.Get("Voces:HubQuitado"), ephemeral: true);
            return;
        }

        await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.HubChannelId = null);
        await ResponderAsync(ctx, _msg.Get("Voces:HubQuitado"));
    }

    [SlashCommand("plantilla", "Personaliza el nombre de los canales temporales (placeholder {usuario}; vacío = por defecto)")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task PlantillaAsync(
        InteractionContext ctx,
        [Option("plantilla", "Plantilla de nombre, p. ej. '🔊 {usuario}'. Vacío = restablecer.")]
        string? plantilla = null)
    {
        if (string.IsNullOrWhiteSpace(plantilla))
        {
            await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.TempChannelNameTemplate = null);
            await ResponderAsync(ctx, _msg.Get("Voces:PlantillaBorrada"));
            return;
        }
        if (plantilla.Length > 100)
        {
            await ResponderAsync(ctx, _msg.Get("Voces:PlantillaLarga"), ephemeral: true);
            return;
        }

        await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.TempChannelNameTemplate = plantilla);
        await ResponderAsync(ctx, _msg.Get("Voces:PlantillaEstablecida",
            ("vista", plantilla.Replace("{usuario}", ctx.User.Username))));
    }
}
