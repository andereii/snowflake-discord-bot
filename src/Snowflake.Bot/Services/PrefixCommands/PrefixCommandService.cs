using System.Text.RegularExpressions;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using Snowflake.Bot.Data.Entities;
using Snowflake.Bot.Modules;
using Snowflake.Bot.Services.AiCommands;
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
    private readonly DeepSeekService _ia;
    private readonly AiCommandConfirmation _confirmaciones;
    private readonly ModerationLogService _modLog;
    private readonly ILogger<PrefixCommandService> _logger;

    public PrefixCommandService(
        DiscordClient client,
        MessagesService msg,
        GuildSettingsService settings,
        CatService cat,
        MusicService music,
        MusicWidgetService widget,
        DeepSeekService ia,
        AiCommandConfirmation confirmaciones,
        ModerationLogService modLog,
        ILogger<PrefixCommandService> logger)
    {
        _client = client;
        _msg = msg;
        _settings = settings;
        _cat = cat;
        _music = music;
        _widget = widget;
        _ia = ia;
        _confirmaciones = confirmaciones;
        _modLog = modLog;
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

    private async Task EjecutarAyudaAsync(MessageCreateEventArgs e)
    {
        var embed = new DiscordEmbedBuilder()
            .WithTitle("❄️ Snowflake — Comandos con prefijo `;`")
            .WithDescription("También puedes usar todos los comandos con la barra diagonal `/`.")
            .AddField("📌 General", "`;ping` — Latencia del bot\n`;gato` — Foto aleatoria de gato\n`;avatar [@usuario]` — Ver avatar\n`;help` — Esta lista de ayuda")
            .AddField("💬 Inteligencia Artificial", "`;talk <texto>` — Habla con la IA\n`;talk-clear` — Reinicia la memoria de la IA")
            .AddField("🎵 Música", "`;play <canción/URL>` — Reproducir música\n`;pause` / `;resume` — Pausar / Reanudar\n`;skip` — Saltar canción\n`;stop` — Detener y salir\n`;queue` — Ver la cola\n`;np` — Canción actual\n`;volume <0-100>` — Ajustar volumen")
            .AddField("🛡️ Moderación", "`;clear <1-100>` — Limpiar mensajes\n`;kick @usuario [motivo]` — Expulsar usuario\n`;ban @usuario [motivo]` — Banear usuario\n`;unban <id> [motivo]` — Desbanear usuario\n`;timeout @usuario <tiempo> [motivo]` — Aislar usuario\n`;warn @usuario [motivo]` — Advertir usuario")
            .WithColor(DiscordColor.Cyan);

        await e.Message.RespondAsync(embed);
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

        var (resultado, puestaEnCola) = await _music.ReproducirAsync(e.Guild.Id, voz.Id, consulta);
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

    // =========================================================================
    // Helpers
    // =========================================================================

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
