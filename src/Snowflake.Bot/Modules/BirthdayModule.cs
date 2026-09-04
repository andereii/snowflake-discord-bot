using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Microsoft.Extensions.Logging;
using Snowflake.Bot.Services;
using Snowflake.Bot.Services.Settings;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Modules;

public class BirthdayModule : SnowflakeModuleBase
{
    private readonly BirthdayService _birthdays;
    private readonly MessagesService _msg;
    private readonly GuildSettingsService _settings;
    private readonly ILogger<BirthdayModule> _logger;

    public BirthdayModule(
        BirthdayService birthdays,
        MessagesService msg,
        GuildSettingsService settings,
        ILogger<BirthdayModule> logger)
    {
        _birthdays = birthdays;
        _msg = msg;
        _settings = settings;
        _logger = logger;
    }

    [SlashCommand("birthday-set", "Set your birthday")]
    [NameLocalization(Localization.Spanish, "cumple-añadir")]
    [NameLocalization(Localization.Portuguese, "aniversário-definir")]
    [DescriptionLocalization(Localization.Spanish, "Registra tu fecha de cumpleaños")]
    [DescriptionLocalization(Localization.Portuguese, "Registra sua data de aniversário")]
    public async Task BirthdaySetAsync(
        InteractionContext ctx,
        [Option("date", "Date in DD/MM/YYYY (or MM/DD/YYYY on English servers)")]
        [NameLocalization(Localization.Spanish, "fecha")]
        [NameLocalization(Localization.Portuguese, "data")]
        [DescriptionLocalization(Localization.Spanish, "Fecha en formato DD/MM/AAAA (o MM/DD/AAAA en servidores en inglés)")]
        [DescriptionLocalization(Localization.Portuguese, "Data no formato DD/MM/AAAA (ou MM/DD/AAAA em servidores em inglês)")]
        string fecha,
        [Option("show_year", "Include your birth year (for age)")]
        [NameLocalization(Localization.Spanish, "mostrar_año")]
        [NameLocalization(Localization.Portuguese, "mostrar_ano")]
        [DescriptionLocalization(Localization.Spanish, "Incluir tu año de nacimiento (para mostrar la edad)")]
        [DescriptionLocalization(Localization.Portuguese, "Incluir seu ano de nascimento (para mostrar a idade)")]
        bool mostrarAnio = false)
    {
        var config = await _settings.GetAsync(ctx.Guild.Id);
        if (!TryParseFecha(fecha, config.Language, out var dia, out var mes, out var anio, out var errorKey))
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, errorKey ?? "Cumple:ErrorFormato"));
            return;
        }

        var (ok, errKey, _) = await _birthdays.RegistrarAsync(
            ctx.Guild.Id, ctx.User.Id, dia, mes, mostrarAnio ? anio : null);

        if (!ok)
        {
            await ResponderErrorAsync(ctx, _msg.Get(ctx.Guild.Id, errKey ?? "Cumple:ErrorFormato"));
            return;
        }

        var desc = anio is not null
            ? _msg.Get(ctx.Guild.Id, "Cumple:RegistradoConAnio",
                ("dia", dia), ("mes", mes), ("anio", anio))
            : _msg.Get(ctx.Guild.Id, "Cumple:Registrado",
                ("dia", dia), ("mes", mes));

        var embed = new DiscordEmbedBuilder()
            .WithTitle(_msg.Get(ctx.Guild.Id, "Cumple:Titulo"))
            .WithDescription(desc)
            .WithColor(DiscordColor.Magenta);

        await ResponderAsync(ctx, embed);
    }

    [SlashCommand("birthday-remove", "Remove your birthday")]
    [NameLocalization(Localization.Spanish, "cumple-quitar")]
    [NameLocalization(Localization.Portuguese, "aniversário-remover")]
    [DescriptionLocalization(Localization.Spanish, "Borra tu fecha de cumpleaños registrada")]
    [DescriptionLocalization(Localization.Portuguese, "Remove sua data de aniversário registrada")]
    public async Task BirthdayRemoveAsync(InteractionContext ctx)
    {
        var quitado = await _birthdays.QuitarAsync(ctx.Guild.Id, ctx.User.Id);
        if (quitado)
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Cumple:Quitado"), ephemeral: true);
        else
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Cumple:NoRegistrado"), ephemeral: true);
    }

    /// <summary>
    /// Parsea la fecha según el idioma del servidor.
    /// EN/PT: MM/DD/YYYY. ES: DD/MM/AAAA.
    /// </summary>
    private bool TryParseFecha(string texto, string lang, out int dia, out int mes, out int? anio, out string? errorKey)
    {
        dia = 0; mes = 0; anio = null; errorKey = null;
        var partes = texto.Replace('-', '/').Split('/');
        if (partes.Length < 2 || partes.Length > 3) { errorKey = "Cumple:ErrorFormato"; return false; }

        // en/pt: MM/DD/YYYY. es: DD/MM/AAAA.
        bool primeroEsMes = lang != Languages.Spanish;

        if (!int.TryParse(partes[0], out var a) || !int.TryParse(partes[1], out var b))
        {
            errorKey = "Cumple:ErrorFormato";
            return false;
        }
        int? year = null;
        if (partes.Length == 3 && int.TryParse(partes[2], out var y))
            year = y;

        if (primeroEsMes)
        {
            mes = a; dia = b;
        }
        else
        {
            dia = a; mes = b;
        }
        anio = year;
        return true;
    }
}
