using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Snowflake.Bot.Data.Entities;
using Snowflake.Bot.Services;
using Snowflake.Bot.Services.Settings;

namespace Snowflake.Bot.Modules;

/// <summary>
/// Juego de conteo con récords, estadísticas, bases alternativas y oportunidades extra.
/// Comandos: /counting canal · desactivar · base · oportunidades · objetivo · objetivo-quitar
///           · iconos · mensaje-perdida · leaderboard · estadisticas
/// </summary>
[SlashCommandGroup("counting", "Configure and play the counting game on the server")]
[DescriptionLocalization(Localization.Spanish, "Configura y juega al conteo en el servidor")]
[DescriptionLocalization(Localization.Portuguese, "Configura e joga o jogo de contagem no servidor")]
public sealed class CountingModule : SnowflakeModuleBase
{
    private readonly GuildSettingsService _settings;
    private readonly CountingService _counting;
    private readonly MessagesService _msg;

    public CountingModule(GuildSettingsService settings, CountingService counting, MessagesService msg)
    {
        _settings = settings;
        _counting = counting;
        _msg = msg;
    }

    // ------------------------- Configuración (admins) -------------------------

    [SlashCommand("channel", "Set the channel where counting happens")]
    [NameLocalization(Localization.Spanish, "canal")]
    [NameLocalization(Localization.Portuguese, "canal")]
    [DescriptionLocalization(Localization.Spanish, "Establece el canal donde se contará")]
    [DescriptionLocalization(Localization.Portuguese, "Define o canal onde a contagem acontece")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task CanalAsync(
        InteractionContext ctx,
        [Option("channel", "Text channel for counting")]
        [NameLocalization(Localization.Spanish, "canal")]
        [NameLocalization(Localization.Portuguese, "canal")]
        [DescriptionLocalization(Localization.Spanish, "Canal de texto para el conteo")]
        [DescriptionLocalization(Localization.Portuguese, "Canal de texto para a contagem")] DiscordChannel canal)
    {
        if (canal.Type != ChannelType.Text)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Conteo:CanalDebeSerTexto"), ephemeral: true);
            return;
        }

        await _settings.UpdateCountingAsync(ctx.Guild.Id, cfg => cfg.ChannelId = canal.Id);
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Conteo:CanalEstablecido", ("canal", canal.Mention)));
    }

    [SlashCommand("disable", "Unlink the channel and stop reading counting")]
    [NameLocalization(Localization.Spanish, "desactivar")]
    [NameLocalization(Localization.Portuguese, "desativar")]
    [DescriptionLocalization(Localization.Spanish, "Desenlaza el canal y deja de leer el conteo")]
    [DescriptionLocalization(Localization.Portuguese, "Desvincula o canal e para de ler a contagem")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task DesactivarAsync(InteractionContext ctx)
    {
        var cfg = await _settings.GetCountingAsync(ctx.Guild.Id);
        if (cfg is null || cfg.ChannelId is null)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Conteo:YaDesactivado"), ephemeral: true);
            return;
        }

        await _settings.UpdateCountingAsync(ctx.Guild.Id, c => c.ChannelId = null);
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Conteo:Desactivado"));
    }

    [SlashCommand("base", "Change the game mode (decimal, binary, octal, hexadecimal)")]
    [NameLocalization(Localization.Spanish, "base")]
    [NameLocalization(Localization.Portuguese, "base")]
    [DescriptionLocalization(Localization.Spanish, "Cambia el modo de juego (decimal, binario, octal, hexadecimal)")]
    [DescriptionLocalization(Localization.Portuguese, "Muda o modo de jogo (decimal, binário, octal, hexadecimal)")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task BaseAsync(
        InteractionContext ctx,
        [Option("base", "Base to count in")]
        [NameLocalization(Localization.Spanish, "base")]
        [NameLocalization(Localization.Portuguese, "base")]
        [DescriptionLocalization(Localization.Spanish, "Base en la que contar")]
        [DescriptionLocalization(Localization.Portuguese, "Base para contar")]
        [Choice("Decimal", "decimal"), Choice("Binary", "binario"), Choice("Octal", "octal"), Choice("Hexadecimal", "hexadecimal")]
        string base_)
    {
        var tipo = base_ switch
        {
            "binario" => CountingBase.Binario,
            "octal" => CountingBase.Octal,
            "hexadecimal" => CountingBase.Hexadecimal,
            _ => CountingBase.Decimal
        };

        await _settings.UpdateCountingAsync(ctx.Guild.Id, cfg => cfg.Base = tipo);
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Conteo:BaseEstablecida", ("base", Capitalizar(base_))));
    }

    [SlashCommand("chances", "Daily extra chances that forgive a mistake (0-10, 0 = disabled)")]
    [NameLocalization(Localization.Spanish, "oportunidades")]
    [NameLocalization(Localization.Portuguese, "chances")]
    [DescriptionLocalization(Localization.Spanish, "Oportunidades extra diarias que perdonan un error (0-10, 0 = desactivado)")]
    [DescriptionLocalization(Localization.Portuguese, "Chances extras diárias que perdoam um erro (0-10, 0 = desativado)")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task OportunidadesAsync(
        InteractionContext ctx,
        [Option("amount", "Number of daily pardons (0 to 10)")]
        [NameLocalization(Localization.Spanish, "cantidad")]
        [NameLocalization(Localization.Portuguese, "quantidade")]
        [DescriptionLocalization(Localization.Spanish, "Número de perdones al día (0 a 10)")]
        [DescriptionLocalization(Localization.Portuguese, "Número de perdões por dia (0 a 10)")] long cantidad)
    {
        if (cantidad < 0 || cantidad > 10)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Conteo:OportunidadesRango"), ephemeral: true);
            return;
        }

        await _settings.UpdateCountingAsync(ctx.Guild.Id, cfg => cfg.ExtraChancesPerDay = (int)cantidad);
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Conteo:OportunidadesActualizadas", ("n", cantidad)));
    }

    [SlashCommand("goal", "Set a numeric goal for the server")]
    [NameLocalization(Localization.Spanish, "objetivo")]
    [NameLocalization(Localization.Portuguese, "objetivo")]
    [DescriptionLocalization(Localization.Spanish, "Establece un objetivo numérico para el servidor")]
    [DescriptionLocalization(Localization.Portuguese, "Define um objetivo numérico para o servidor")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task ObjetivoAsync(
        InteractionContext ctx,
        [Option("number", "Goal number to reach")]
        [NameLocalization(Localization.Spanish, "numero")]
        [NameLocalization(Localization.Portuguese, "numero")]
        [DescriptionLocalization(Localization.Spanish, "Número objetivo que alcanzar")]
        [DescriptionLocalization(Localization.Portuguese, "Número do objetivo a alcançar")] long numero)
    {
        if (numero <= 0)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Conteo:ObjetivoInvalido"), ephemeral: true);
            return;
        }

        var cfg = await _settings.UpdateCountingAsync(ctx.Guild.Id, c => c.Goal = numero);
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Conteo:ObjetivoEstablecido",
            ("objetivo", CountingService.Formatear(numero, cfg.Base))));
    }

    [SlashCommand("goal-remove", "Remove the server goal")]
    [NameLocalization(Localization.Spanish, "objetivo-quitar")]
    [NameLocalization(Localization.Portuguese, "objetivo-remover")]
    [DescriptionLocalization(Localization.Spanish, "Elimina el objetivo del servidor")]
    [DescriptionLocalization(Localization.Portuguese, "Remove o objetivo do servidor")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task ObjetivoQuitarAsync(InteractionContext ctx)
    {
        await _settings.UpdateCountingAsync(ctx.Guild.Id, cfg => cfg.Goal = null);
        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Conteo:ObjetivoQuitado"));
    }

    [SlashCommand("icons", "Choose the emojis the bot reacts with (correct, incorrect, record)")]
    [NameLocalization(Localization.Spanish, "iconos")]
    [NameLocalization(Localization.Portuguese, "icones")]
    [DescriptionLocalization(Localization.Spanish, "Elige los emojis con los que el bot reacciona (correcto, incorrecto, récord)")]
    [DescriptionLocalization(Localization.Portuguese, "Escolhe os emojis com que o bot reage (correto, incorreto, recorde)")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task IconosAsync(
        InteractionContext ctx,
        [Option("correct", "Correct answer emoji (default ✅)")]
        [NameLocalization(Localization.Spanish, "correcto")]
        [NameLocalization(Localization.Portuguese, "correto")]
        [DescriptionLocalization(Localization.Spanish, "Emoji de respuesta correcta (por defecto ✅)")]
        [DescriptionLocalization(Localization.Portuguese, "Emoji de resposta correta (padrão ✅)")] string? correcto = null,
        [Option("incorrect", "Incorrect answer emoji (default ❌)")]
        [NameLocalization(Localization.Spanish, "incorrecto")]
        [NameLocalization(Localization.Portuguese, "incorreto")]
        [DescriptionLocalization(Localization.Spanish, "Emoji de respuesta incorrecta (por defecto ❌)")]
        [DescriptionLocalization(Localization.Portuguese, "Emoji de resposta incorreta (padrão ❌)")] string? incorrecto = null,
        [Option("record", "New record emoji (default 🎉)")]
        [NameLocalization(Localization.Spanish, "record")]
        [NameLocalization(Localization.Portuguese, "recorde")]
        [DescriptionLocalization(Localization.Spanish, "Emoji de nuevo récord (por defecto 🎉)")]
        [DescriptionLocalization(Localization.Portuguese, "Emoji de novo recorde (padrão 🎉)")] string? record = null)
    {
        // Validar los que se hayan proporcionado.
        if (correcto is not null && !CountingService.EmojiValido(ctx.Client, correcto)
            || incorrecto is not null && !CountingService.EmojiValido(ctx.Client, incorrecto)
            || record is not null && !CountingService.EmojiValido(ctx.Client, record))
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Conteo:IconoInvalido"), ephemeral: true);
            return;
        }

        await _settings.UpdateCountingAsync(ctx.Guild.Id, cfg =>
        {
            if (correcto is not null) cfg.EmojiCorrect = correcto.Trim();
            if (incorrecto is not null) cfg.EmojiIncorrect = incorrecto.Trim();
            if (record is not null) cfg.EmojiRecord = record.Trim();
        });

        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Conteo:IconosActualizados",
            ("correcto", correcto ?? CountingService.EmojiCorrectoPorDefecto),
            ("incorrecto", incorrecto ?? CountingService.EmojiIncorrectoPorDefecto),
            ("record", record ?? CountingService.EmojiRecordPorDefecto)));
    }

    [SlashCommand("lose-message", "Customize the message when the count is lost (placeholders: {cuenta} {usuario} {siguiente})")]
    [NameLocalization(Localization.Spanish, "mensaje-perdida")]
    [NameLocalization(Localization.Portuguese, "mensagem-perda")]
    [DescriptionLocalization(Localization.Spanish, "Personaliza el mensaje al perder la cuenta (placeholders: {cuenta} {usuario} {siguiente})")]
    [DescriptionLocalization(Localization.Portuguese, "Personaliza a mensagem ao perder a contagem (placeholders: {cuenta} {usuario} {siguiente})")]
    [SlashRequirePermissions(Permissions.ManageGuild)]
    public async Task MensajePerdidaAsync(
        InteractionContext ctx,
        [Option("message", "New message. Empty = reset to default.")]
        [NameLocalization(Localization.Spanish, "mensaje")]
        [NameLocalization(Localization.Portuguese, "mensagem")]
        [DescriptionLocalization(Localization.Spanish, "Nuevo mensaje. Vacío = restablecer al por defecto.")]
        [DescriptionLocalization(Localization.Portuguese, "Nova mensagem. Vazio = redefinir para a padrão.")]
        string? mensaje = null)
    {
        if (string.IsNullOrWhiteSpace(mensaje))
        {
            await _settings.UpdateCountingAsync(ctx.Guild.Id, cfg => cfg.LoseMessage = null);
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Conteo:MensajePerdidaBorrado"));
            return;
        }

        var cfg = await _settings.UpdateCountingAsync(ctx.Guild.Id, c => c.LoseMessage = mensaje);

        // Vista previa sustituyendo con quien ejecuta el comando.
        var vista = mensaje
            .Replace("{cuenta}", CountingService.Formatear(42, cfg.Base))
            .Replace("{usuario}", ctx.User.Mention)
            .Replace("{siguiente}", CountingService.Formatear(1, cfg.Base));

        await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Conteo:MensajePerdidaGuardado", ("vista", vista)));
    }

    // ------------------------- Consulta (todos) -------------------------

    [SlashCommand("leaderboard", "Show who has contributed the most to the count")]
    [NameLocalization(Localization.Spanish, "leaderboard")]
    [NameLocalization(Localization.Portuguese, "leaderboard")]
    [DescriptionLocalization(Localization.Spanish, "Muestra quién más ha aportado a la cuenta")]
    [DescriptionLocalization(Localization.Portuguese, "Mostra quem mais contribuiu para a contagem")]
    public async Task LeaderboardAsync(InteractionContext ctx)
    {
        var embed = await _counting.ConstruirLeaderboardAsync(ctx.Guild);
        if (embed is null)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Conteo:LeaderboardVacio"), ephemeral: true);
            return;
        }
        await ResponderAsync(ctx, embed);
    }

    [SlashCommand("stats", "Show a user's stats (or your own)")]
    [NameLocalization(Localization.Spanish, "estadisticas")]
    [NameLocalization(Localization.Portuguese, "estatisticas")]
    [DescriptionLocalization(Localization.Spanish, "Muestra las estadísticas de un usuario (o de ti mismo)")]
    [DescriptionLocalization(Localization.Portuguese, "Mostra as estatísticas de um usuário (ou as suas)")]
    public async Task EstadisticasAsync(
        InteractionContext ctx,
        [Option("user", "User to look up (empty = yourself)")]
        [NameLocalization(Localization.Spanish, "usuario")]
        [NameLocalization(Localization.Portuguese, "usuário")]
        [DescriptionLocalization(Localization.Spanish, "Usuario a consultar (vacío = tú mismo)")]
        [DescriptionLocalization(Localization.Portuguese, "Usuário a consultar (vazio = você mesmo)")] DiscordUser? usuario = null)
    {
        var uid = usuario?.Id ?? ctx.User.Id;
        var embed = await _counting.ConstruirStatsAsync(ctx.Guild, uid);
        if (embed is null)
        {
            await ResponderAsync(ctx, _msg.Get(ctx.Guild.Id, "Conteo:StatsSinDatos"), ephemeral: true);
            return;
        }
        await ResponderAsync(ctx, embed);
    }

    // ------------------------- Ayudantes -------------------------

    private static string Capitalizar(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
