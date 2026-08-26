using Microsoft.EntityFrameworkCore;
using Snowflake.Bot.Data;
using System.Text;
using System.Text.RegularExpressions;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snowflake.Bot.Configuration;
using Snowflake.Bot.Data.Entities;
using Snowflake.Bot.Modules;
using Snowflake.Bot.Services.AiCommands;
using Snowflake.Bot.Services.Calculators;
using Snowflake.Bot.Services.Settings;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Services.PrefixCommands;

/// <summary>
/// Despachador de comandos tradicionales por texto con prefijo ';'
/// Soporta comandos generales, multimedia, IA y moderación sin requerir la barra '/'.
/// </summary>
public sealed class PrefixCommandService
{
    public const char Prefijo = ';';

    private readonly DiscordClient _client;
    private readonly MessagesService _msg;
    private readonly GuildSettingsService _settings;
    private readonly CatService _cat;
    private readonly MusicService _music;
    private readonly MusicWidgetService _widget;
    private readonly DownloadService _dl;
    private readonly LitterboxService _litter;
    private readonly CalculatorService _calc;
    private readonly TriviaService _trivia;
    private readonly AfkService _afk;
    private readonly DeepSeekService _ia;
    private readonly AiCommandConfirmation _confirmaciones;
    private readonly ModerationLogService _modLog;
    private readonly IOptionsMonitor<DownloadOptions> _dlOptions;
    private readonly IOptionsMonitor<BotConfiguration> _config;
    private readonly IDbContextFactory<BotDbContext> _dbFactory;
    private readonly ILogger<PrefixCommandService> _logger;

    public PrefixCommandService(
        DiscordClient client,
        MessagesService msg,
        GuildSettingsService settings,
        CatService cat,
        MusicService music,
        MusicWidgetService widget,
        DownloadService dl,
        LitterboxService litter,
        CalculatorService calc,
        TriviaService trivia,
        AfkService afk,
        DeepSeekService ia,
        AiCommandConfirmation confirmaciones,
        ModerationLogService modLog,
        IOptionsMonitor<DownloadOptions> dlOptions,
        IOptionsMonitor<BotConfiguration> config,
        IDbContextFactory<BotDbContext> dbFactory,
        ILogger<PrefixCommandService> logger)
    {
        _client = client;
        _msg = msg;
        _settings = settings;
        _cat = cat;
        _music = music;
        _widget = widget;
        _dl = dl;
        _litter = litter;
        _calc = calc;
        _trivia = trivia;
        _afk = afk;
        _ia = ia;
        _confirmaciones = confirmaciones;
        _modLog = modLog;
        _dlOptions = dlOptions;
        _config = config;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Comprueba si el mensaje empieza con ';' y ejecuta el comando si corresponde.
    /// Devuelve true si el mensaje fue procesado como comando.
    /// </summary>
    public async Task<bool> ProcesarMensajeAsync(MessageCreateEventArgs e)
    {
        if (e.Guild is null || e.Author.IsBot) return false;

        var contenido = e.Message.Content?.Trim();
        if (string.IsNullOrEmpty(contenido) || !contenido.StartsWith(Prefijo))
            return false;

        // Evitar falsos positivos con emoticonos como ';)', ';-', ';_;'
        var sinPrefijo = contenido[1..].TrimStart();
        if (string.IsNullOrWhiteSpace(sinPrefijo)) return false;

        var partes = ParsearArgumentos(sinPrefijo);
        if (partes.Count == 0) return false;

        var cmd = partes[0].ToLowerInvariant();
        var args = partes.Skip(1).ToList();

        try
        {
            switch (cmd)
            {
                // ================= General =================
                case "ping":
                    await EjecutarPingAsync(e);
                    return true;

                case "gato":
                case "cat":
                    await EjecutarGatoAsync(e);
                    return true;

                case "avatar":
                case "av":
                    await EjecutarAvatarAsync(e, args);
                    return true;

                case "help":
                case "ayuda":
                case "comandos":
                case "commands":
                    await EjecutarAyudaAsync(e);
                    return true;

                // ================= Calculadora y Matemáticas =================
                case "calc":
                case "calcular":
                case "calculadora":
                case "math":
                    await EjecutarCalcAsync(e, sinPrefijo[cmd.Length..].Trim());
                    return true;

                // ================= Trivia =================
                case "trivia":
                case "t":
                    await EjecutarTriviaAsync(e, args);
                    return true;

                // ================= AFK =================
                case "afk":
                    await EjecutarAfkAsync(e, args, sinPrefijo[cmd.Length..].Trim());
                    return true;

                // ================= Descargas =================
                case "download":
                case "descargar":
                case "baixar":
                case "d":
                    await EjecutarDescargarAsync(e, args);
                    return true;

                // ================= IA =================
                case "talk":
                case "charlar":
                case "conversar":
                    await EjecutarChatIaAsync(e, string.Join(' ', args));
                    return true;

                case "talk-clear":
                case "charlar-limpiar":
                case "conversar-limpar":
                    await EjecutarChatLimpiarAsync(e);
                    return true;

                // ================= Música =================
                case "play":
                case "p":
                    await EjecutarPlayAsync(e, string.Join(' ', args));
                    return true;

                case "skip":
                case "s":
                case "saltar":
                    await EjecutarSkipAsync(e);
                    return true;

                case "pause":
                case "pausa":
                    await EjecutarPauseAsync(e);
                    return true;

                case "resume":
                case "reanudar":
                case "reanuda":
                    await EjecutarResumeAsync(e);
                    return true;

                case "stop":
                case "parar":
                case "detener":
                    await EjecutarStopAsync(e);
                    return true;

                case "queue":
                case "cola":
                case "q":
                    await EjecutarQueueAsync(e);
                    return true;

                case "np":
                case "nowplaying":
                    await EjecutarNowPlayingAsync(e);
                    return true;

                case "volume":
                case "volumen":
                case "vol":
                    await EjecutarVolumenAsync(e, args.FirstOrDefault());
                    return true;

                case "lavalink":
                case "lavalink-status":
                    await EjecutarLavalinkStatusAsync(e);
                    return true;

                // ================= Moderación / Utilidad =================
                case "clear":
                case "limpiar":
                case "purge":
                    await EjecutarClearAsync(e, args.FirstOrDefault());
                    return true;

                case "kick":
                case "expulsar":
                    await EjecutarKickAsync(e, args);
                    return true;

                case "softban":
                    await EjecutarSoftbanAsync(e, args);
                    return true;
                case "hardmute":
                    await EjecutarHardmuteAsync(e, args);
                    return true;
                case "unhardmute":
                    await EjecutarUnhardmuteAsync(e, args);
                    return true;
                case "shuffle":
                    await EjecutarShuffleAsync(e);
                    return true;
                case "jump":
                case "seek":
                    await EjecutarJumpAsync(e, args.FirstOrDefault());
                    return true;


                case "ban":
                case "banear":
                case "vetar":
                    await EjecutarBanAsync(e, args);
                    return true;

                case "unban":
                case "desbanear":
                    await EjecutarUnbanAsync(e, args);
                    return true;

                case "timeout":
                case "aislar":
                case "mute":
                    await EjecutarTimeoutAsync(e, args);
                    return true;

                case "warn":
                case "advertir":
                    await EjecutarWarnAsync(e, args);
                    return true;

                case "role":
                case "rol":
                case "r":
                    await EjecutarRoleAsync(e, args);
                    return true;

                default:
                    // Comando desconocido: no hacemos nada para evitar molestar en el chat
                    return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ejecutando comando de prefijo ;{Comando} en {Guild}", cmd, e.Guild.Id);
            try
            {
                await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Errores:Interno")}");
            }
            catch { }
            return true;
        }
    }

    // =========================================================================
    // Implementaciones de comandos
    // =========================================================================

    private async Task EjecutarPingAsync(MessageCreateEventArgs e)
    {
        var latencia = _client.Ping;
        await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Ping:Respuesta", ("latencia", latencia)));
    }

    private async Task EjecutarGatoAsync(MessageCreateEventArgs e)
    {
        var fotoUrl = await _cat.ObtenerFotoGatoAsync();
        if (string.IsNullOrWhiteSpace(fotoUrl))
        {
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Gato:Error"));
            return;
        }

        var titulo = CatModule.GenerarTituloMew();
        var embed = new DiscordEmbedBuilder()
            .WithTitle(titulo)
            .WithImageUrl(fotoUrl)
            .WithFooter(fotoUrl)
            .WithColor(new DiscordColor("#f9c2d1"));

        await e.Message.RespondAsync(embed);
    }

    private async Task EjecutarAvatarAsync(MessageCreateEventArgs e, List<string> args)
    {
        DiscordUser usuario = e.Author;
        if (args.Count > 0 && e.Message.MentionedUsers.Count > 0)
        {
            usuario = e.Message.MentionedUsers[0];
        }
        else if (args.Count > 0 && ulong.TryParse(args[0], out var id))
        {
            try { usuario = await _client.GetUserAsync(id); }
            catch { }
        }

        var avatarUrl = usuario.GetAvatarUrl(ImageFormat.Auto, 1024) ?? usuario.DefaultAvatarUrl;
        var embed = new DiscordEmbedBuilder()
            .WithTitle(usuario.Username)
            .WithImageUrl(avatarUrl)
            .WithFooter(avatarUrl)
            .WithColor(DiscordColor.Azure);

        await e.Message.RespondAsync(embed);
    }

    private async Task EjecutarDescargarAsync(MessageCreateEventArgs e, List<string> args)
    {
        // Interruptor por servidor
        if (!(await _settings.GetAsync(e.Guild.Id)).DownloadsEnabled)
        {
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Descargas:Desactivado"));
            return;
        }

        if (args.Count == 0)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} Uso: `;descargar <URL> [video|audio]` (o `;d <URL>`)");
            return;
        }

        string url = "";
        bool soloAudio = false;

        // Detectar si el usuario pasó formato antes o después de la URL
        foreach (var arg in args)
        {
            if (arg.Equals("audio", StringComparison.OrdinalIgnoreCase) || arg.Equals("mp3", StringComparison.OrdinalIgnoreCase))
            {
                soloAudio = true;
            }
            else if (arg.Equals("video", StringComparison.OrdinalIgnoreCase) || arg.Equals("mp4", StringComparison.OrdinalIgnoreCase))
            {
                soloAudio = false;
            }
            else if (Uri.TryCreate(arg, UriKind.Absolute, out var uri) && (uri.Scheme is "http" or "https"))
            {
                url = arg;
            }
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Descargas:UrlInvalida"));
            return;
        }

        await e.Channel.TriggerTypingAsync();
        DiscordMessage? progreso = null;
        try
        {
            progreso = await e.Message.RespondAsync("⏳ Descargando y procesando el archivo...");
        }
        catch { }

        string? tempDir = null;
        try
        {
            var timeout = TimeSpan.FromMinutes(Math.Max(1, _dlOptions.CurrentValue.TimeoutMinutes));
            using var cts = new CancellationTokenSource(timeout);
            var res = await _dl.DescargarAsync(url, soloAudio, cts.Token);
            tempDir = res.TempDir;

            var size = new FileInfo(res.FilePath).Length;
            var maxBytes = _dlOptions.CurrentValue.MaxDiscordBytes;

            if (size <= maxBytes)
            {
                await using var fs = File.OpenRead(res.FilePath);
                var builder = new DiscordMessageBuilder()
                    .WithContent(_msg.Get(e.Guild.Id, "Descargas:Exito", ("titulo", res.Title)));
                builder.AddFile(Path.GetFileName(res.FilePath), fs);

                if (progreso is not null)
                {
                    try { await progreso.DeleteAsync(); } catch { }
                }

                await e.Message.RespondAsync(builder);
            }
            else
            {
                var enlace = await _litter.SubirAsync(
                    res.FilePath, Path.GetFileName(res.FilePath), CancellationToken.None);

                var sizeMB = size / (1024.0 * 1024.0);
                var embed = new DiscordEmbedBuilder()
                    .WithTitle(res.Title)
                    .WithDescription(_msg.Get(e.Guild.Id, "Descargas:DemasiadoGrandeEmbed",
                        ("tamano", sizeMB.ToString("0.#")),
                        ("enlace", enlace)))
                    .WithUrl(enlace)
                    .WithColor(DiscordColor.Azure)
                    .WithFooter(_msg.Get(e.Guild.Id, "Descargas:PieLitterbox"));

                if (progreso is not null)
                {
                    await progreso.ModifyAsync(new DiscordMessageBuilder().AddEmbed(embed));
                }
                else
                {
                    await e.Message.RespondAsync(embed);
                }
            }
        }
        catch (YtDlpException ex)
        {
            var debug = _config.CurrentValue.Debug;
            var texto = debug
                ? _msg.Get(e.Guild.Id, "Descargas:Error", ("detalles", ex.Message))
                : _msg.Get(e.Guild.Id, "Descargas:ErrorGenerico");

            if (progreso is not null)
                await progreso.ModifyAsync(texto);
            else
                await e.Message.RespondAsync(texto);
        }
        catch (Exception ex)
        {
            var debug = _config.CurrentValue.Debug;
            var texto = debug
                ? _msg.Get(e.Guild.Id, "Descargas:ErrorInterno", ("tipo", ex.GetType().Name), ("mensaje", ex.Message))
                : _msg.Get(e.Guild.Id, "Descargas:ErrorGenerico");

            if (progreso is not null)
                await progreso.ModifyAsync(texto);
            else
                await e.Message.RespondAsync(texto);
        }
        finally
        {
            if (tempDir is not null)
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    private async Task EjecutarAyudaAsync(MessageCreateEventArgs e)
    {
        var embed = new DiscordEmbedBuilder()
            .WithTitle("❄️ Snowflake — Comandos con prefijo `;`")
            .WithDescription("También puedes usar todos los comandos con la barra diagonal `/`.")
            .AddField("📌 General y Multimedia", "`;ping` — Latencia del bot\n`;gato` — Foto aleatoria de gato\n`;calc <expresión/problema>` — Calculadora y resolución con IA\n`;afk [motivo]` — Establecer estado ausente\n`;trivia [categoría] [dificultad]` — Jugar a la trivia cultural\n`;descargar <URL> [audio]` — Descargar vídeo/audio de Internet\n`;avatar [@usuario]` — Ver avatar\n`;help` — Esta lista de ayuda")
            .AddField("💬 Inteligencia Artificial", "`;talk <texto>` — Habla con la IA\n`;talk-clear` — Reinicia la memoria de la IA")
            .AddField("🎵 Música", "`;play <canción/URL>` — Reproducir música\n`;pause` / `;resume` — Pausar / Reanudar\n`;skip` — Saltar canción\n`;stop` — Detener y salir\n`;queue` — Ver la cola\n`;np` — Canción actual\n`;volume <0-100>` — Ajustar volumen")
            .AddField("🛡️ Moderación y Roles", "`;afk mod <ignore|unignore|ignored|list|remove|removeall|reset>` — Gestión de AFK\n`;role <add|remove> @user <rol>` — Gestionar roles\n`;clear <1-100>` — Limpiar mensajes\n`;kick @usuario [motivo]` — Expulsar usuario\n`;ban @usuario [motivo]` — Banear usuario\n`;unban <id> [motivo]` — Desbanear usuario\n`;timeout @usuario <tiempo> [motivo]` — Aislar usuario\n`;warn @usuario [motivo]` — Advertir usuario")
            .WithColor(DiscordColor.Cyan);

        await e.Message.RespondAsync(embed);
    }

    private async Task EjecutarTriviaAsync(MessageCreateEventArgs e, IReadOnlyList<string> args)
    {
        if (args.Count > 0 && args[0].Equals("stats", StringComparison.OrdinalIgnoreCase))
        {
            ulong targetId = e.Author.Id;
            if (args.Count > 1 && ulong.TryParse(Regex.Match(args[1], @"\d+").Value, out var idMencion))
                targetId = idMencion;

            var stat = await _trivia.ObtenerEstadisticasAsync(e.Guild.Id, targetId);
            var member = await e.Guild.GetMemberAsync(targetId);

            if (stat is null || stat.TotalAnswers == 0)
            {
                await e.Message.RespondAsync($"ℹ️ **{member.DisplayName}** aún no ha jugado ninguna partida de trivia.");
                return;
            }

            var precision = stat.TotalAnswers > 0 ? (stat.CorrectAnswers * 100 / stat.TotalAnswers) : 0;
            var embedStats = new DiscordEmbedBuilder()
                .WithTitle($"🏆 {_msg.Get(e.Guild.Id, "Trivia:TituloStats", ("usuario", member.DisplayName))}")
                .WithThumbnail(member.AvatarUrl)
                .WithColor(DiscordColor.Gold)
                .AddField($"⭐ {_msg.Get(e.Guild.Id, "Trivia:PuntosTotales")}", $"`{stat.Score}` pts", inline: true)
                .AddField($"🔥 {_msg.Get(e.Guild.Id, "Trivia:RachaActual")}", $"`{stat.CurrentStreak}` (Mejor: `{stat.BestStreak}`)", inline: true)
                .AddField($"🎯 {_msg.Get(e.Guild.Id, "Trivia:Precision")}", $"`{precision}%` ({stat.CorrectAnswers}/{stat.TotalAnswers})", inline: true)
                .WithFooter($"Snowflake Trivia • {e.Guild.Name}");

            await e.Message.RespondAsync(embedStats);
            return;
        }

        if (args.Count > 0 && (args[0].Equals("top", StringComparison.OrdinalIgnoreCase) ||
                                args[0].Equals("leaderboard", StringComparison.OrdinalIgnoreCase) ||
                                args[0].Equals("ranking", StringComparison.OrdinalIgnoreCase)))
        {
            var top = await _trivia.ObtenerLeaderboardAsync(e.Guild.Id, 10);
            if (top.Count == 0)
            {
                await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Trivia:SinRanking"));
                return;
            }

            var sb = new StringBuilder();
            var medallas = new[] { "🥇", "🥈", "🥉" };
            for (int i = 0; i < top.Count; i++)
            {
                var s = top[i];
                var icono = i < 3 ? medallas[i] : $"**#{i + 1}**";
                var precision = s.TotalAnswers > 0 ? (s.CorrectAnswers * 100 / s.TotalAnswers) : 0;
                sb.AppendLine($"{icono} <@{s.UserId}> — **{s.Score} pts** (`{s.CorrectAnswers}/{s.TotalAnswers}` aciertos • {precision}%)");
            }

            var embedTop = new DiscordEmbedBuilder()
                .WithTitle($"🏆 {_msg.Get(e.Guild.Id, "Trivia:TituloRanking")}")
                .WithDescription(sb.ToString())
                .WithColor(DiscordColor.Gold)
                .WithFooter($"Snowflake Trivia • {e.Guild.Name}");

            await e.Message.RespondAsync(embedTop);
            return;
        }

        string? categoria = args.Count > 0 ? args[0] : null;
        string? dificultad = args.Count > 1 ? args[1] : null;

        await _trivia.JugarPrefixAsync(e, categoria, dificultad);
    }

    private async Task EjecutarAfkAsync(MessageCreateEventArgs e, IReadOnlyList<string> args, string restoTexto)
    {
        var miembro = e.Message.Author as DiscordMember ?? await e.Guild.GetMemberAsync(e.Author.Id);

        // Subcomando de moderación: ;afk mod ...
        if (args.Count > 0 && args[0].Equals("mod", StringComparison.OrdinalIgnoreCase))
        {
            if (!miembro.Permissions.HasPermission(Permissions.ManageGuild))
            {
                await e.Message.RespondAsync($"{BotEmojis.Error} Necesitas el permiso `Gestionar Servidor` para usar los comandos de moderación de AFK.");
                return;
            }

            var subArgs = args.Skip(1).ToList();
            if (subArgs.Count == 0)
            {
                await e.Message.RespondAsync($"ℹ️ Uso: `;afk mod <ignore|unignore|ignored|list|remove|removeall|reset>`");
                return;
            }

            var accion = subArgs[0].ToLowerInvariant();
            switch (accion)
            {
                case "ignore":
                    if (subArgs.Count < 2 || !ulong.TryParse(Regex.Match(subArgs[1], @"\d+").Value, out var chIgnoreId) || !e.Guild.Channels.TryGetValue(chIgnoreId, out var chIgnore))
                    {
                        await e.Message.RespondAsync($"{BotEmojis.Error} Uso: `;afk mod ignore <#canal>`");
                        return;
                    }
                    var agreg = await _afk.AgregarCanalIgnoradoAsync(e.Guild.Id, chIgnore.Id);
                    await e.Message.RespondAsync(agreg
                        ? $"✅ {_msg.Get(e.Guild.Id, "Afk:CanalIgnorado", ("canal", chIgnore.Mention))}"
                        : $"ℹ️ {_msg.Get(e.Guild.Id, "Afk:CanalYaIgnorado", ("canal", chIgnore.Mention))}");
                    return;

                case "unignore":
                    if (subArgs.Count < 2 || !ulong.TryParse(Regex.Match(subArgs[1], @"\d+").Value, out var chUnignoreId) || !e.Guild.Channels.TryGetValue(chUnignoreId, out var chUnignore))
                    {
                        await e.Message.RespondAsync($"{BotEmojis.Error} Uso: `;afk mod unignore <#canal>`");
                        return;
                    }
                    var rem = await _afk.RemoverCanalIgnoradoAsync(e.Guild.Id, chUnignore.Id);
                    await e.Message.RespondAsync(rem
                        ? $"✅ {_msg.Get(e.Guild.Id, "Afk:CanalDesignorado", ("canal", chUnignore.Mention))}"
                        : $"⚠️ {_msg.Get(e.Guild.Id, "Afk:CanalNoIgnorado", ("canal", chUnignore.Mention))}");
                    return;

                case "ignored":
                    var ignorados = _afk.ObtenerCanalesIgnorados(e.Guild.Id);
                    if (ignorados.Count == 0)
                    {
                        await e.Message.RespondAsync($"ℹ️ {_msg.Get(e.Guild.Id, "Afk:SinCanalesIgnorados")}");
                        return;
                    }
                    var sbIgn = new StringBuilder();
                    foreach (var cId in ignorados)
                        sbIgn.AppendLine($"• <#{cId}> (`{cId}`)");

                    var embedIgn = new DiscordEmbedBuilder()
                        .WithTitle($"🔇 {_msg.Get(e.Guild.Id, "Afk:TituloCanalesIgnorados")}")
                        .WithDescription(sbIgn.ToString())
                        .WithColor(DiscordColor.CornflowerBlue)
                        .WithFooter($"Total: {ignorados.Count}");
                    await e.Message.RespondAsync(embedIgn);
                    return;

                case "list":
                    var ausentesMod = _afk.ListarAfk(e.Guild.Id);
                    if (ausentesMod.Count == 0)
                    {
                        await e.Message.RespondAsync($"ℹ️ {_msg.Get(e.Guild.Id, "Afk:SinMiembrosAusentes")}");
                        return;
                    }
                    var ahoraMod = DateTimeOffset.UtcNow;
                    var sbAusMod = new StringBuilder();
                    foreach (var a in ausentesMod)
                    {
                        var tiempoFmt = DurationParser.Format(ahoraMod - a.SetAt, _msg.Locale(e.Guild.Id));
                        sbAusMod.AppendLine($"• <@{a.UserId}> — *\"{a.Reason}\"* (`{tiempoFmt}`)");
                    }
                    var embedAusMod = new DiscordEmbedBuilder()
                        .WithTitle($"💤 {_msg.Get(e.Guild.Id, "Afk:TituloMiembrosAusentes")}")
                        .WithDescription(sbAusMod.ToString())
                        .WithColor(DiscordColor.CornflowerBlue)
                        .WithFooter($"Total: {ausentesMod.Count}");
                    await e.Message.RespondAsync(embedAusMod);
                    return;

                case "remove":
                    if (subArgs.Count < 2 || !ulong.TryParse(Regex.Match(subArgs[1], @"\d+").Value, out var uRemId))
                    {
                        await e.Message.RespondAsync($"{BotEmojis.Error} Uso: `;afk mod remove <@usuario>`");
                        return;
                    }
                    var mRem = await e.Guild.GetMemberAsync(uRemId);
                    var remOk = await _afk.RemoverAfkAsync(e.Guild, mRem);
                    await e.Message.RespondAsync(remOk
                        ? $"✅ {_msg.Get(e.Guild.Id, "Afk:RemovidoMod", ("usuario", mRem.DisplayName))}"
                        : $"⚠️ {_msg.Get(e.Guild.Id, "Afk:NoEstaAusente", ("usuario", mRem.DisplayName))}");
                    return;

                case "removeall":
                    var totalRem = await _afk.RemoverTodosAfkAsync(e.Guild);
                    await e.Message.RespondAsync(totalRem > 0
                        ? $"✅ {_msg.Get(e.Guild.Id, "Afk:RemovidosTodos", ("total", totalRem.ToString()))}"
                        : $"ℹ️ {_msg.Get(e.Guild.Id, "Afk:SinMiembrosAusentes")}");
                    return;

                case "reset":
                    if (subArgs.Count < 2 || !ulong.TryParse(Regex.Match(subArgs[1], @"\d+").Value, out var uRstId))
                    {
                        await e.Message.RespondAsync($"{BotEmojis.Error} Uso: `;afk mod reset <@usuario>`");
                        return;
                    }
                    var mRst = await e.Guild.GetMemberAsync(uRstId);
                    var rstOk = await _afk.ResetearMotivoAfkAsync(e.Guild.Id, uRstId);
                    await e.Message.RespondAsync(rstOk
                        ? $"✅ {_msg.Get(e.Guild.Id, "Afk:MotivoReseteado", ("usuario", mRst.DisplayName))}"
                        : $"⚠️ {_msg.Get(e.Guild.Id, "Afk:NoEstaAusente", ("usuario", mRst.DisplayName))}");
                    return;

                default:
                    await e.Message.RespondAsync($"{BotEmojis.Error} Subcomando de moderación desconocido. Opciones: `ignore`, `unignore`, `ignored`, `list`, `remove`, `removeall`, `reset`");
                    return;
            }
        }

        // Subcomando ;afk list (acceso general de lectura)
        if (args.Count > 0 && args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            var ausentes = _afk.ListarAfk(e.Guild.Id);
            if (ausentes.Count == 0)
            {
                await e.Message.RespondAsync($"ℹ️ {_msg.Get(e.Guild.Id, "Afk:SinMiembrosAusentes")}");
                return;
            }

            var ahora = DateTimeOffset.UtcNow;
            var sb = new StringBuilder();
            foreach (var a in ausentes)
            {
                var tiempoFmt = DurationParser.Format(ahora - a.SetAt, _msg.Locale(e.Guild.Id));
                sb.AppendLine($"• <@{a.UserId}> — *\"{a.Reason}\"* (`{tiempoFmt}`)");
            }

            var embed = new DiscordEmbedBuilder()
                .WithTitle($"💤 {_msg.Get(e.Guild.Id, "Afk:TituloMiembrosAusentes")}")
                .WithDescription(sb.ToString())
                .WithColor(DiscordColor.CornflowerBlue)
                .WithFooter($"Total: {ausentes.Count}");

            await e.Message.RespondAsync(embed);
            return;
        }

        // Establecer AFK personal: ;afk [motivo...]
        var motivo = string.IsNullOrWhiteSpace(restoTexto) ? null : restoTexto.Trim();
        await _afk.EstablecerAfkAsync(e.Guild, miembro, motivo);

        var motivoFmt = string.IsNullOrWhiteSpace(motivo) ? "AFK" : motivo;
        var resp = _msg.Get(e.Guild.Id, "Afk:Establecido",
            ("usuario", e.Author.Username),
            ("motivo", motivoFmt));

        var embedAfk = new DiscordEmbedBuilder()
            .WithTitle(e.Author.Username)
            .WithThumbnail(e.Author.AvatarUrl)
            .WithDescription($"💤 {resp}")
            .WithColor(DiscordColor.CornflowerBlue);

        await e.Message.RespondAsync(embedAfk);
    }

    private async Task EjecutarCalcAsync(MessageCreateEventArgs e, string entrada)
    {
        if (string.IsNullOrWhiteSpace(entrada))
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} Uso: `;calc <expresión o problema en texto>` (ej. `;calc 5^2 + sqrt(144)` o `;calc 3(9/3/2)`)");
            return;
        }

        var miembro = e.Message.Author as DiscordMember ?? await e.Guild.GetMemberAsync(e.Author.Id);
        await e.Channel.TriggerTypingAsync();
        var res = await _calc.ProcesarAsync(e.Guild, e.Channel, miembro, entrada);

        if (res.EsIa && !string.IsNullOrEmpty(res.TextoIa))
        {
            await e.Message.RespondAsync(res.TextoIa);
        }
        else if (res.Embed is not null)
        {
            await e.Message.RespondAsync(res.Embed);
        }
    }

    private async Task EjecutarChatIaAsync(MessageCreateEventArgs e, string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Chat:Truncada")}");
            return;
        }

        var miembro = e.Message.Author as DiscordMember ?? await e.Guild.GetMemberAsync(e.Author.Id);
        var aiCtx = new AiCommandContext(_client, e.Guild, e.Channel, miembro);

        DiscordMessage? mensajeBot = null;
        try
        {
            mensajeBot = await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Chat:Pensando"));
            var outcome = await _ia.PreguntarAsync(aiCtx, e.Author.Username, texto);

            if (outcome.HayPendiente)
            {
                try { await mensajeBot.DeleteAsync(); } catch { }
                await _confirmaciones.EnviarNormalAsync(e.Channel, outcome.Pendiente!, aiCtx, outcome.Pendiente!.DescripcionComando);
                return;
            }

            var contenido = ChatResponseFormatter.Formatear(outcome.Texto ?? "", _msg.Get(e.Guild.Id, "Chat:Truncada"));
            var builder = new DiscordMessageBuilder().WithContent(contenido);
            foreach (var comando in outcome.Comandos)
                builder.AddEmbed(ChatModule.ConstruirEmbedComando(comando));

            await mensajeBot.ModifyAsync(builder);
            _ia.RegistrarMensajeGenerado(mensajeBot.Id, e.Guild.Id);
        }
        catch (DeepSeekBusyException)
        {
            if (mensajeBot is not null)
                await mensajeBot.ModifyAsync(_msg.Get(e.Guild.Id, "Chat:Ocupado"));
        }
        catch (DeepSeekConfirmationPendingException)
        {
            if (mensajeBot is not null)
                await mensajeBot.ModifyAsync(_msg.Get(e.Guild.Id, "Chat:ConfirmacionEnCurso"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error en ;talk en {Guild}", e.Guild.Id);
            if (mensajeBot is not null)
                await mensajeBot.ModifyAsync(_msg.Get(e.Guild.Id, "Chat:Error"));
        }
    }

    private async Task EjecutarChatLimpiarAsync(MessageCreateEventArgs e)
    {
        var miembro = e.Message.Author as DiscordMember ?? await e.Guild.GetMemberAsync(e.Author.Id);
        if (miembro is null || !miembro.Permissions.HasPermission(Permissions.ManageGuild))
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Errores:SinPermisos")}");
            return;
        }

        var borrada = _ia.Limpiar(e.Guild.Id);
        var clave = borrada ? "Chat:LimpiadoExito" : "Chat:LimpiadoVacio";
        await e.Message.RespondAsync(_msg.Get(e.Guild.Id, clave));
    }

    // ================= Música =================

    private async Task EjecutarPlayAsync(MessageCreateEventArgs e, string consulta)
    {
        var miembro = e.Message.Author as DiscordMember ?? await e.Guild.GetMemberAsync(e.Author.Id);
        var voz = miembro?.VoiceState?.Channel;
        if (voz is null)
        {
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Musica:NoEnCanal"));
            return;
        }

        if (string.IsNullOrWhiteSpace(consulta))
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} Especifica el nombre de la canción o un enlace.");
            return;
        }

        if (!await _music.EstaOnlineAsync())
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Musica:ErrorLavalinkOffline")}");
            return;
        }

        (Lavalink4NET.Rest.Entities.Tracks.TrackLoadResult resultado, bool puestaEnCola) datos;
        try
        {
            datos = await _music.ReproducirAsync(e.Guild.Id, voz.Id, consulta);
        }
        catch (LavalinkUnavailableException)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Musica:ErrorLavalinkOffline")}");
            return;
        }
        catch (Exception)
        {
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Musica:ErrorLavalink"));
            return;
        }

        var (resultado, puestaEnCola) = datos;
        if (!resultado.IsSuccess)
        {
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Musica:NoEncontrado"));
            return;
        }

        if (resultado.IsPlaylist)
        {
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Musica:PlaylistAnadida",
                ("titulo", resultado.Playlist?.Name ?? "Playlist"),
                ("n", resultado.Count)));
        }
        else if (puestaEnCola)
        {
            var track = resultado.Track!;
            var embed = new DiscordEmbedBuilder()
                .WithDescription(_msg.Get(e.Guild.Id, "Musica:PuestaEnCola",
                    ("titulo", track.Title), ("autor", track.Author)))
                .WithColor(DiscordColor.Blurple);

            var art = MusicService.ArtworkUrl(track);
            if (!string.IsNullOrEmpty(art))
                embed.WithThumbnail(art);

            await e.Message.RespondAsync(embed);
        }
        else
        {
            var track = resultado.Track!;
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Musica:Tocando",
                ("titulo", track.Title),
                ("autor", track.Author),
                ("duracion", MusicService.FormatearDuracion(track.Duration, track.IsLiveStream, _msg.Get(e.Guild.Id, "Musica:EnVivo")))));

            await _widget.EnviarOActualizarAsync(e.Channel, e.Guild.Id);
        }
    }

    private async Task EjecutarSkipAsync(MessageCreateEventArgs e)
    {
        var (ok, msgClave) = await ValidarControlMusicaAsync(e);
        if (!ok) { await e.Message.RespondAsync(_msg.Get(e.Guild.Id, msgClave)); return; }

        var siguiente = await _music.SaltarAsync(e.Guild.Id);
        var texto = siguiente is null
            ? _msg.Get(e.Guild.Id, "Musica:SaltadoVacio")
            : _msg.Get(e.Guild.Id, "Musica:SaltadoProxima",
                ("titulo", siguiente.Title),
                ("autor", siguiente.Author));

        await e.Message.RespondAsync(texto);
        await _widget.RefrescarSiExisteAsync(e.Guild.Id, e.Channel);
    }

    private async Task EjecutarPauseAsync(MessageCreateEventArgs e)
    {
        var (ok, msgClave) = await ValidarControlMusicaAsync(e);
        if (!ok) { await e.Message.RespondAsync(_msg.Get(e.Guild.Id, msgClave)); return; }

        await _music.PausarAsync(e.Guild.Id);
        await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Musica:Pausado"));
        await _widget.RefrescarSiExisteAsync(e.Guild.Id, e.Channel);
    }

    private async Task EjecutarResumeAsync(MessageCreateEventArgs e)
    {
        var (ok, msgClave) = await ValidarControlMusicaAsync(e);
        if (!ok) { await e.Message.RespondAsync(_msg.Get(e.Guild.Id, msgClave)); return; }

        await _music.ReanudarAsync(e.Guild.Id);
        await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Musica:Reanudado"));
        await _widget.RefrescarSiExisteAsync(e.Guild.Id, e.Channel);
    }

    private async Task EjecutarStopAsync(MessageCreateEventArgs e)
    {
        var (ok, msgClave) = await ValidarControlMusicaAsync(e);
        if (!ok) { await e.Message.RespondAsync(_msg.Get(e.Guild.Id, msgClave)); return; }

        await _music.DetenerAsync(e.Guild.Id);
        await _widget.DetenerAsync(e.Guild.Id);
        await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Musica:Detenido"));
    }

    private async Task EjecutarQueueAsync(MessageCreateEventArgs e)
    {
        var embed = _music.ConstruirEmbedCola(e.Guild.Id, _msg);
        if (embed is null)
        {
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Musica:ColaVacia"));
            return;
        }

        await e.Message.RespondAsync(embed);
    }

    private async Task EjecutarNowPlayingAsync(MessageCreateEventArgs e)
    {
        var player = _music.Obtener(e.Guild.Id);
        var actual = player?.CurrentTrack;
        if (actual is null)
        {
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Musica:NoActivo"));
            return;
        }

        var embed = new DiscordEmbedBuilder()
            .WithTitle(_msg.Get(e.Guild.Id, "Musica:Reproduciendo", ("titulo", actual.Title)))
            .WithDescription($"⏱️ Duración: `{actual.Duration:mm\\:ss}`\n👤 Autor: {actual.Author}")
            .WithColor(DiscordColor.SpringGreen);

        var art = MusicService.ArtworkUrl(actual);
        if (!string.IsNullOrEmpty(art))
            embed.WithThumbnail(art);

        await e.Message.RespondAsync(embed);
    }

    private async Task EjecutarVolumenAsync(MessageCreateEventArgs e, string? valorStr)
    {
        var (ok, msgClave) = await ValidarControlMusicaAsync(e);
        if (!ok) { await e.Message.RespondAsync(_msg.Get(e.Guild.Id, msgClave)); return; }

        if (string.IsNullOrWhiteSpace(valorStr) || !int.TryParse(valorStr, out var vol) || vol < 0 || vol > 100)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} Usa `;volume <0-100>`.");
            return;
        }

        var aplicado = await _music.VolumenAsync(e.Guild.Id, vol);
        await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Musica:Volumen", ("nivel", aplicado)));
    }

    private async Task EjecutarLavalinkStatusAsync(MessageCreateEventArgs e)
    {
        var embed = await _music.ConstruirEmbedEstadoLavalinkAsync(e.Guild.Id, _msg);
        await e.Message.RespondAsync(embed);
    }

    private async Task<(bool Ok, string MensajeClave)> ValidarControlMusicaAsync(MessageCreateEventArgs e)
    {
        var miembro = e.Message.Author as DiscordMember ?? await e.Guild.GetMemberAsync(e.Author.Id);
        if (_music.Obtener(e.Guild.Id) is null)
            return (false, "Musica:NoActivo");

        var (puede, mensaje) = await _music.ValidarControlAsync(e.Guild, miembro, _msg);
        if (!puede)
            return (false, mensaje ?? "Musica:NoEnMismoCanal");

        return (true, string.Empty);
    }

    // ================= Moderación / Utilidad =================

    private async Task EjecutarClearAsync(MessageCreateEventArgs e, string? cantidadStr)
    {
        var miembro = e.Message.Author as DiscordMember ?? await e.Guild.GetMemberAsync(e.Author.Id);
        if (miembro is null || !e.Channel.PermissionsFor(miembro).HasPermission(Permissions.ManageMessages))
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Limpiar:SinPermisosCanal")}");
            return;
        }

        var bot = e.Guild.CurrentMember;
        if (bot is null || !e.Channel.PermissionsFor(bot).HasPermission(Permissions.ManageMessages))
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Limpiar:SinPermisosBotCanal")}");
            return;
        }

        if (string.IsNullOrWhiteSpace(cantidadStr) || !int.TryParse(cantidadStr, out var cantidad) || cantidad < 1 || cantidad > 100)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Limpiar:SinCantidad")}");
            return;
        }

        // Borramos el propio mensaje del comando primero
        try { await e.Message.DeleteAsync(); } catch { }

        var mensajes = await e.Channel.GetMessagesAsync(cantidad);
        var ahora = DateTimeOffset.UtcNow;
        var borrables = mensajes.Where(m => (ahora - m.CreationTimestamp) < TimeSpan.FromDays(14)).ToList();
        var viejos = mensajes.Where(m => (ahora - m.CreationTimestamp) >= TimeSpan.FromDays(14)).ToList();

        var borrados = 0;
        if (borrables.Count > 0)
        {
            if (borrables.Count == 1)
            {
                try { await e.Channel.DeleteMessageAsync(borrables[0]); borrados++; } catch { }
            }
            else
            {
                await e.Channel.DeleteMessagesAsync(borrables);
                borrados += borrables.Count;
            }
        }

        foreach (var m in viejos)
        {
            try { await e.Channel.DeleteMessageAsync(m); borrados++; } catch { }
            await Task.Delay(500);
        }

        var resultado = _msg.Get(e.Guild.Id, "Limpiar:Exito", ("n", borrados), ("canal", e.Channel.Mention));
        var aviso = await e.Channel.SendMessageAsync(resultado);
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            try { await aviso.DeleteAsync(); } catch { }
        });
    }

    private async Task EjecutarKickAsync(MessageCreateEventArgs e, List<string> args)
    {
        var miembroAutor = e.Message.Author as DiscordMember ?? await e.Guild.GetMemberAsync(e.Author.Id);
        if (miembroAutor is null || !miembroAutor.Permissions.HasPermission(Permissions.KickMembers))
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Errores:SinPermisos")}");
            return;
        }

        var target = await ObtenerUsuarioObjetivoAsync(e, args);
        if (target is null)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} Uso: `;kick @usuario [motivo]`");
            return;
        }

        var targetMember = await e.Guild.GetMemberAsync(target.Id);
        if (targetMember is null)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Moderacion:Errores:NoEnServidor", ("usuario", target.Username))}");
            return;
        }

        if (targetMember.Hierarchy >= miembroAutor.Hierarchy && !miembroAutor.IsOwner)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} No puedes expulsar a un miembro con rol igual o superior al tuyo.");
            return;
        }

        var bot = e.Guild.CurrentMember;
        if (bot is null || targetMember.Hierarchy >= bot.Hierarchy)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Moderacion:Errores:Jerarquia", ("usuario", target.Username))}");
            return;
        }

        var motivo = args.Count > 1 ? string.Join(' ', args.Skip(1)) : _msg.Get(e.Guild.Id, "Moderacion:MotivoPorDefecto");
        await targetMember.RemoveAsync(motivo);

        var incidente = await _modLog.RegistrarAsync(e.Guild.Id, target, e.Author, IncidentType.Expulsion, motivo);
        await _modLog.AnunciarAsync(e.Guild, incidente);
        await e.Message.RespondAsync($"{BotEmojis.Check} {_msg.Get(e.Guild.Id, "Moderacion:Exito:Expulsion", ("usuario", target.Username))}");
    }

    private async Task EjecutarBanAsync(MessageCreateEventArgs e, List<string> args)
    {
        var miembroAutor = e.Message.Author as DiscordMember ?? await e.Guild.GetMemberAsync(e.Author.Id);
        if (miembroAutor is null || !miembroAutor.Permissions.HasPermission(Permissions.BanMembers))
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Errores:SinPermisos")}");
            return;
        }

        var target = await ObtenerUsuarioObjetivoAsync(e, args);
        if (target is null)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} Uso: `;ban @usuario [motivo]`");
            return;
        }

        if (e.Guild.Members.TryGetValue(target.Id, out var targetMember))
        {
            if (targetMember.Hierarchy >= miembroAutor.Hierarchy && !miembroAutor.IsOwner)
            {
                await e.Message.RespondAsync($"{BotEmojis.Error} No puedes banear a un miembro con rol igual o superior al tuyo.");
                return;
            }
            var bot = e.Guild.CurrentMember;
            if (bot is null || targetMember.Hierarchy >= bot.Hierarchy)
            {
                await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Moderacion:Errores:Jerarquia", ("usuario", target.Username))}");
                return;
            }
        }

        var motivo = args.Count > 1 ? string.Join(' ', args.Skip(1)) : _msg.Get(e.Guild.Id, "Moderacion:MotivoPorDefecto");
        await e.Guild.BanMemberAsync(target.Id, 0, motivo);

        var incidente = await _modLog.RegistrarAsync(e.Guild.Id, target, e.Author, IncidentType.Veto, motivo);
        await _modLog.AnunciarAsync(e.Guild, incidente);
        await e.Message.RespondAsync($"{BotEmojis.Check} {_msg.Get(e.Guild.Id, "Moderacion:Exito:Veto", ("usuario", target.Username))}");
    }

    private async Task EjecutarUnbanAsync(MessageCreateEventArgs e, List<string> args)
    {
        var miembroAutor = e.Message.Author as DiscordMember ?? await e.Guild.GetMemberAsync(e.Author.Id);
        if (miembroAutor is null || !miembroAutor.Permissions.HasPermission(Permissions.BanMembers))
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Errores:SinPermisos")}");
            return;
        }

        if (args.Count == 0 || !ulong.TryParse(Regex.Match(args[0], @"\d+").Value, out var targetId))
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} Uso: `;unban <ID_o_mención> [motivo]`");
            return;
        }

        var motivo = args.Count > 1 ? string.Join(' ', args.Skip(1)) : _msg.Get(e.Guild.Id, "Moderacion:MotivoPorDefecto");
        await e.Guild.UnbanMemberAsync(targetId, motivo);
        await e.Message.RespondAsync($"{BotEmojis.Check} Usuario desbaneado.");
    }

    private async Task EjecutarTimeoutAsync(MessageCreateEventArgs e, List<string> args)
    {
        var miembroAutor = e.Message.Author as DiscordMember ?? await e.Guild.GetMemberAsync(e.Author.Id);
        if (miembroAutor is null || !miembroAutor.Permissions.HasPermission(Permissions.ModerateMembers))
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Errores:SinPermisos")}");
            return;
        }

        var target = await ObtenerUsuarioObjetivoAsync(e, args);
        if (target is null || args.Count < 2)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} Uso: `;timeout @usuario <duración (ej: 10m, 1h)> [motivo]`");
            return;
        }

        var targetMember = await e.Guild.GetMemberAsync(target.Id);
        if (targetMember is null)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Moderacion:Errores:NoEnServidor", ("usuario", target.Username))}");
            return;
        }

        if (!DurationParser.TryParse(args[1], out var duracion) || duracion <= TimeSpan.Zero)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Moderacion:Errores:DuracionInvalida")}");
            return;
        }

        var motivo = args.Count > 2 ? string.Join(' ', args.Skip(2)) : _msg.Get(e.Guild.Id, "Moderacion:MotivoPorDefecto");
        var hasta = DateTimeOffset.UtcNow + duracion;
        await targetMember.TimeoutAsync(hasta, motivo);

        var incidente = await _modLog.RegistrarAsync(e.Guild.Id, target, e.Author, IncidentType.Aislamiento, motivo, duracion);
        await _modLog.AnunciarAsync(e.Guild, incidente);
        await e.Message.RespondAsync($"{BotEmojis.Check} {_msg.Get(e.Guild.Id, "Moderacion:Exito:Aislamiento", ("usuario", target.Username), ("duracion", DurationParser.Format(duracion, "es")))}");
    }

    private async Task EjecutarWarnAsync(MessageCreateEventArgs e, List<string> args)
    {
        var miembroAutor = e.Message.Author as DiscordMember ?? await e.Guild.GetMemberAsync(e.Author.Id);
        if (miembroAutor is null || !miembroAutor.Permissions.HasPermission(Permissions.ManageMessages))
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Errores:SinPermisos")}");
            return;
        }

        var target = await ObtenerUsuarioObjetivoAsync(e, args);
        if (target is null)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} Uso: `;warn @usuario [motivo]`");
            return;
        }

        var motivo = args.Count > 1 ? string.Join(' ', args.Skip(1)) : _msg.Get(e.Guild.Id, "Moderacion:MotivoPorDefecto");
        var incidente = await _modLog.RegistrarAsync(e.Guild.Id, target, e.Author, IncidentType.Advertencia, motivo);
        await _modLog.AnunciarAsync(e.Guild, incidente);
        await e.Message.RespondAsync($"{BotEmojis.Check} {_msg.Get(e.Guild.Id, "Moderacion:Exito:Advertencia", ("usuario", target.Username))}");
    }

    private async Task EjecutarRoleAsync(MessageCreateEventArgs e, List<string> args)
    {
        var miembroAutor = e.Message.Author as DiscordMember ?? await e.Guild.GetMemberAsync(e.Author.Id);
        if (miembroAutor is null || !miembroAutor.Permissions.HasPermission(Permissions.ManageRoles))
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Errores:SinPermisos")}");
            return;
        }

        if (args.Count < 3)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} Uso: `;role <add|remove> <@usuario> <rol>` (o `;rol <agregar|quitar> <@usuario> <rol>`)");
            return;
        }

        var subcmd = args[0].ToLowerInvariant();
        bool esAgregar = subcmd is "add" or "agregar" or "anadir" or "añadir" or "+";
        bool esQuitar = subcmd is "remove" or "quitar" or "remover" or "-";

        if (!esAgregar && !esQuitar)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} Subcomando inválido. Usa `;role add ...` o `;role remove ...`");
            return;
        }

        var usuarioArg = args[1];
        var rolArg = string.Join(' ', args.Skip(2));

        var targetUser = await ObtenerUsuarioObjetivoAsync(e, [usuarioArg]);
        if (targetUser is null)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Moderacion:NoMiembro")}");
            return;
        }

        var targetMiembro = await e.Guild.GetMemberAsync(targetUser.Id);
        if (targetMiembro is null)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Moderacion:NoMiembro")}");
            return;
        }

        var rol = ResolverRolPrefix(e.Guild, rolArg, e);
        if (rol is null)
        {
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Roles:NoEncontrado"));
            return;
        }

        if (e.Guild.CurrentMember.Hierarchy <= rol.Position)
        {
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Roles:JerarquiaBot", ("rol", rol.Name)));
            return;
        }

        if (e.Guild.OwnerId != miembroAutor.Id && miembroAutor.Hierarchy <= rol.Position)
        {
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Roles:JerarquiaUsuario", ("rol", rol.Name)));
            return;
        }

        if (esAgregar)
        {
            if (targetMiembro.Roles.Any(r => r.Id == rol.Id))
            {
                await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Roles:YaTiene", ("usuario", targetMiembro.DisplayName), ("rol", rol.Name)));
                return;
            }

            await targetMiembro.GrantRoleAsync(rol, $"Asignado por {miembroAutor.Username} ({miembroAutor.Id})");
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Roles:Asignado", ("usuario", targetMiembro.DisplayName), ("rol", rol.Name)));
        }
        else
        {
            if (!targetMiembro.Roles.Any(r => r.Id == rol.Id))
            {
                await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Roles:NoTiene", ("usuario", targetMiembro.DisplayName), ("rol", rol.Name)));
                return;
            }

            await targetMiembro.RevokeRoleAsync(rol, $"Quitado por {miembroAutor.Username} ({miembroAutor.Id})");
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Roles:Removido", ("usuario", targetMiembro.DisplayName), ("rol", rol.Name)));
        }
    }


    private async Task EjecutarSoftbanAsync(MessageCreateEventArgs e, List<string> args)
    {
        var miembroAutor = e.Message.Author as DiscordMember ?? await e.Guild.GetMemberAsync(e.Author.Id);
        if (miembroAutor is null || !miembroAutor.Permissions.HasPermission(Permissions.BanMembers))
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Errores:SinPermisos")}");
            return;
        }

        var target = await ObtenerUsuarioObjetivoAsync(e, args);
        if (target is null)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} Uso: `;softban @usuario [motivo]`");
            return;
        }

        var motivo = args.Count > 1 ? string.Join(' ', args.Skip(1)) : _msg.Get(e.Guild.Id, "Moderacion:MotivoPorDefecto");
        
        await e.Guild.BanMemberAsync(target.Id, 7, motivo);
        await e.Guild.UnbanMemberAsync(target.Id, "Softban: unban automático");

        var incidente = await _modLog.RegistrarAsync(e.Guild.Id, target, e.Author, IncidentType.Softban, motivo);
        await _modLog.AnunciarAsync(e.Guild, incidente);
        await e.Message.RespondAsync($"{BotEmojis.Check} {_msg.Get(e.Guild.Id, "Moderacion:Exito:Softban", ("usuario", target.Username))}");
    }

    private async Task EjecutarHardmuteAsync(MessageCreateEventArgs e, List<string> args)
    {
        var miembroAutor = e.Message.Author as DiscordMember ?? await e.Guild.GetMemberAsync(e.Author.Id);
        if (miembroAutor is null || !miembroAutor.Permissions.HasPermission(Permissions.ManageRoles))
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Errores:SinPermisos")}");
            return;
        }

        var targetUser = await ObtenerUsuarioObjetivoAsync(e, args);
        if (targetUser is null)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} Uso: `;hardmute @usuario [motivo]`");
            return;
        }
        
        var target = await e.Guild.GetMemberAsync(targetUser.Id);
        if (target is null) return;

        var motivo = args.Count > 1 ? string.Join(' ', args.Skip(1)) : _msg.Get(e.Guild.Id, "Moderacion:MotivoPorDefecto");
        
        var rolesQuitar = target.Roles
            .Where(r => r.Id != e.Guild.EveryoneRole.Id && !r.IsManaged && r.Position < e.Guild.CurrentMember.Hierarchy)
            .ToList();

        if (rolesQuitar.Count > 0)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var backup = await db.HardmuteBackups.FirstOrDefaultAsync(h => h.GuildId == e.Guild.Id && h.UserId == target.Id);
            var idsTexto = string.Join(",", rolesQuitar.Select(r => r.Id));
            if (backup is null)
            {
                db.HardmuteBackups.Add(new HardmuteBackup { GuildId = e.Guild.Id, UserId = target.Id, RoleIds = idsTexto });
            }
            else
            {
                backup.RoleIds = idsTexto;
                backup.CreatedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync();

            foreach (var rol in rolesQuitar)
            {
                try { await target.RevokeRoleAsync(rol, $"Hardmute por {miembroAutor.Username}"); } catch { }
            }
        }

        foreach (var canal in e.Guild.Channels.Values)
        {
            if (canal.Type is not (ChannelType.Text or ChannelType.Voice or ChannelType.PublicThread or ChannelType.PrivateThread or ChannelType.News or ChannelType.Stage or ChannelType.GuildForum)) continue;
            try
            {
                await canal.AddOverwriteAsync(target, deny: Permissions.SendMessages | Permissions.Speak | Permissions.SendMessagesInThreads, reason: $"Hardmute por {miembroAutor.Username}");
            }
            catch { }
        }

        var incidente = await _modLog.RegistrarAsync(e.Guild.Id, target, e.Author, IncidentType.Hardmute, motivo);
        await _modLog.AnunciarAsync(e.Guild, incidente);
        await e.Message.RespondAsync($"{BotEmojis.Check} {_msg.Get(e.Guild.Id, "Moderacion:Exito:Hardmute", ("usuario", target.Username))}");
    }

    private async Task EjecutarUnhardmuteAsync(MessageCreateEventArgs e, List<string> args)
    {
        var miembroAutor = e.Message.Author as DiscordMember ?? await e.Guild.GetMemberAsync(e.Author.Id);
        if (miembroAutor is null || !miembroAutor.Permissions.HasPermission(Permissions.ManageRoles))
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Errores:SinPermisos")}");
            return;
        }

        var targetUser = await ObtenerUsuarioObjetivoAsync(e, args);
        if (targetUser is null)
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} Uso: `;unhardmute @usuario [motivo]`");
            return;
        }
        
        var target = await e.Guild.GetMemberAsync(targetUser.Id);
        if (target is null) return;

        var motivo = args.Count > 1 ? string.Join(' ', args.Skip(1)) : _msg.Get(e.Guild.Id, "Moderacion:MotivoPorDefecto");
        
        await using var db = await _dbFactory.CreateDbContextAsync();
        var backup = await db.HardmuteBackups.FirstOrDefaultAsync(h => h.GuildId == e.Guild.Id && h.UserId == target.Id);
        if (backup is not null)
        {
            var roleIds = backup.RoleIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => ulong.TryParse(s, out var id) ? id : 0).Where(id => id != 0);
            foreach (var roleId in roleIds)
            {
                var rol = e.Guild.GetRole(roleId);
                if (rol is not null && !rol.IsManaged && rol.Position < e.Guild.CurrentMember.Hierarchy)
                {
                    try { await target.GrantRoleAsync(rol, $"Unhardmute por {miembroAutor.Username}"); } catch { }
                }
            }
            db.HardmuteBackups.Remove(backup);
            await db.SaveChangesAsync();
        }

        foreach (var canal in e.Guild.Channels.Values)
        {
            var overwrite = canal.PermissionOverwrites?.FirstOrDefault(o => o.Id == target.Id && o.Type == OverwriteType.Member);
            if (overwrite is not null)
            {
                try { await overwrite.DeleteAsync($"Unhardmute: {motivo}"); } catch { }
            }
        }

        var incidente = await _modLog.RegistrarAsync(e.Guild.Id, target, e.Author, IncidentType.FinHardmute, motivo);
        await _modLog.AnunciarAsync(e.Guild, incidente);
        await e.Message.RespondAsync($"{BotEmojis.Check} {_msg.Get(e.Guild.Id, "Moderacion:Exito:FinHardmute", ("usuario", target.Username))}");
    }

    private async Task EjecutarShuffleAsync(MessageCreateEventArgs e)
    {
        var (ok, msgClave) = await ValidarControlMusicaAsync(e);
        if (!ok) { await e.Message.RespondAsync(_msg.Get(e.Guild.Id, msgClave)); return; }

        if (_music.AleorizarCola(e.Guild.Id))
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Musica:Aleatorizado"));
        else
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Musica:ColaVacia"));
    }

    private async Task EjecutarJumpAsync(MessageCreateEventArgs e, string? valorStr)
    {
        var (ok, msgClave) = await ValidarControlMusicaAsync(e);
        if (!ok) { await e.Message.RespondAsync(_msg.Get(e.Guild.Id, msgClave)); return; }

        if (string.IsNullOrWhiteSpace(valorStr) || !MusicService.TryParseTimestamp(valorStr, out var ts))
        {
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Musica:TimestampInvalido")}");
            return;
        }

        if (await _music.SaltarAPosicionAsync(e.Guild.Id, ts))
            await e.Message.RespondAsync(_msg.Get(e.Guild.Id, "Musica:SaltadoA", ("posicion", MusicService.FormatearDuracion(ts, false))));
        else
            await e.Message.RespondAsync($"{BotEmojis.Error} {_msg.Get(e.Guild.Id, "Musica:ErrorSaltar")}");
    }
    // =========================================================================
    // Helpers
    // =========================================================================

    private DiscordRole? ResolverRolPrefix(DiscordGuild guild, string input, MessageCreateEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();

        if (e.Message.MentionedRoles.Count > 0)
        {
            var matchId = Regex.Match(input, @"\d+");
            if (matchId.Success && ulong.TryParse(matchId.Value, out var mId))
            {
                var rolMencionado = e.Message.MentionedRoles.FirstOrDefault(r => r.Id == mId);
                if (rolMencionado is not null) return rolMencionado;
            }
        }

        var match = Regex.Match(input, @"\d+");
        if (match.Success && ulong.TryParse(match.Value, out var id) && guild.Roles.TryGetValue(id, out var rId))
            return rId;

        foreach (var r in guild.Roles.Values)
        {
            if (r.Name.Equals(input, StringComparison.OrdinalIgnoreCase))
                return r;
        }

        foreach (var r in guild.Roles.Values)
        {
            if (r.Name.Contains(input, StringComparison.OrdinalIgnoreCase))
                return r;
        }

        return null;
    }

    private async Task<DiscordUser?> ObtenerUsuarioObjetivoAsync(MessageCreateEventArgs e, List<string> args)
    {
        if (e.Message.MentionedUsers.Count > 0)
            return e.Message.MentionedUsers[0];

        if (args.Count > 0)
        {
            var match = Regex.Match(args[0], @"\d+");
            if (match.Success && ulong.TryParse(match.Value, out var id))
            {
                try { return await _client.GetUserAsync(id); }
                catch { }
            }
        }

        return null;
    }

    private static List<string> ParsearArgumentos(string input)
    {
        var resultado = new List<string>();
        if (string.IsNullOrWhiteSpace(input)) return resultado;

        var regex = new Regex(@"(?:""(?<match>[^""]*)""|(?<match>\S+))", RegexOptions.Compiled);
        foreach (Match m in regex.Matches(input))
        {
            resultado.Add(m.Groups["match"].Value);
        }

        return resultado;
    }
}
