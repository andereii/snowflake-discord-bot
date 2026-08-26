using System.Text.Json.Nodes;
using DSharpPlus;
using DSharpPlus.Entities;
using Lavalink4NET.Players.Queued;
using Microsoft.EntityFrameworkCore;
using Snowflake.Bot.Data.Entities;

namespace Snowflake.Bot.Services.AiCommands;

/// <summary>Música, configuración de módulos y estado del servidor (tools de IA).</summary>
public sealed partial class AiCommandExecutor
{
    // ------------------------- música -------------------------

    private IEnumerable<ToolDef> CatalogoMusica()
    {
        yield return new ToolDef(
            "music_play",
            "Play a song or playlist in the voice channel of the user who is talking (URL or search).",
            Esquema(("query", "string", "YouTube/Spotify URL or search terms.")),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                var desc = "/m play";
                var query = ArgString(args, "query");
                if (string.IsNullOrWhiteSpace(query))
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Musica:NoEncontrado"), desc);

                var voz = ctx.Miembro.VoiceState?.Channel;
                if (voz is null)
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Musica:NoEnCanal"), desc);

                if (!await _music.EstaOnlineAsync().ConfigureAwait(false))
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Musica:ErrorLavalinkOffline"), desc);

                (Lavalink4NET.Rest.Entities.Tracks.TrackLoadResult resultado, bool puestaEnCola) datos;
                try
                {
                    datos = await _music.ReproducirAsync(ctx.Guild.Id, voz.Id, query).ConfigureAwait(false);
                }
                catch (LavalinkUnavailableException)
                {
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Musica:ErrorLavalinkOffline"), desc);
                }
                catch (Exception)
                {
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Musica:ErrorLavalink"), desc);
                }

                var (resultado, puestaEnCola) = datos;
                if (!resultado.IsSuccess)
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Musica:NoEncontrado"), desc);

                string texto;
                if (resultado.IsPlaylist)
                    texto = _msg.Get(ctx.Guild.Id, "Musica:PlaylistAnadida",
                        ("titulo", resultado.Playlist?.Name ?? "Playlist"), ("n", resultado.Count));
                else if (puestaEnCola)
                    texto = _msg.Get(ctx.Guild.Id, "Musica:PuestaEnCola",
                        ("titulo", resultado.Track!.Title), ("autor", resultado.Track.Author));
                else
                {
                    var track = resultado.Track!;
                    texto = _msg.Get(ctx.Guild.Id, "Musica:Tocando",
                        ("titulo", track.Title),
                        ("autor", track.Author),
                        ("duracion", MusicService.FormatearDuracion(track.Duration, track.IsLiveStream,
                            _msg.Get(ctx.Guild.Id, "Musica:EnVivo"))));
                    await _widget.EnviarOActualizarAsync(ctx.Canal, ctx.Guild.Id).ConfigureAwait(false);
                }

                return new AiCommandResult(true, texto, desc);
            });

        yield return new ToolDef(
            "music_skip",
            "Skip the current song.",
            Esquema(),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                var desc = "/m skip";
                if (_music.Obtener(ctx.Guild.Id) is null)
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Musica:NoActivo"), desc);
                if (await _music.ValidarControlAsync(ctx.Guild, ctx.Miembro, _msg) is { Puede: false } control)
                    return new AiCommandResult(false, control.MensajeError!, desc);

                var siguiente = await _music.SaltarAsync(ctx.Guild.Id).ConfigureAwait(false);
                var texto = siguiente is null
                    ? _msg.Get(ctx.Guild.Id, "Musica:SaltadoVacio")
                    : _msg.Get(ctx.Guild.Id, "Musica:SaltadoProxima",
                        ("titulo", siguiente.Title), ("autor", siguiente.Author));
                await _widget.RefrescarSiExisteAsync(ctx.Guild.Id, ctx.Canal).ConfigureAwait(false);
                return new AiCommandResult(true, texto, desc);
            });

        yield return new ToolDef(
            "music_pause",
            "Pause the current song.",
            Esquema(),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                var desc = "/m pause";
                if (_music.Obtener(ctx.Guild.Id) is null)
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Musica:NoActivo"), desc);
                if (await _music.ValidarControlAsync(ctx.Guild, ctx.Miembro, _msg) is { Puede: false } control)
                    return new AiCommandResult(false, control.MensajeError!, desc);

                await _music.PausarAsync(ctx.Guild.Id).ConfigureAwait(false);
                await _widget.RefrescarSiExisteAsync(ctx.Guild.Id, ctx.Canal).ConfigureAwait(false);
                return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Musica:Pausado"), desc);
            });

        yield return new ToolDef(
            "music_resume",
            "Resume the paused playback.",
            Esquema(),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                var desc = "/m resume";
                if (_music.Obtener(ctx.Guild.Id) is null)
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Musica:NoActivo"), desc);
                if (await _music.ValidarControlAsync(ctx.Guild, ctx.Miembro, _msg) is { Puede: false } control)
                    return new AiCommandResult(false, control.MensajeError!, desc);

                await _music.ReanudarAsync(ctx.Guild.Id).ConfigureAwait(false);
                await _widget.RefrescarSiExisteAsync(ctx.Guild.Id, ctx.Canal).ConfigureAwait(false);
                return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Musica:Reanudado"), desc);
            });

        yield return new ToolDef(
            "music_stop",
            "Stop the music and disconnect the bot.",
            Esquema(),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                var desc = "/m stop";
                if (await _music.ValidarControlAsync(ctx.Guild, ctx.Miembro, _msg) is { Puede: false } control)
                    return new AiCommandResult(false, control.MensajeError!, desc);

                await _music.DetenerAsync(ctx.Guild.Id).ConfigureAwait(false);
                await _widget.DetenerAsync(ctx.Guild.Id).ConfigureAwait(false);
                return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Musica:Detenido"), desc);
            });

        yield return new ToolDef(
            "music_volume",
            "Change the music volume. Accepts an absolute number (0-100), a relative adjustment like -10 or +5, or a simple expression like 30+20.",
            Esquema(("level", "string", "Volume: number (0-100), relative (-10, +5) or simple expression (30+20).")),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                var desc = "/m volume";
                var nivel = ArgString(args, "level");
                if (string.IsNullOrWhiteSpace(nivel))
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Musica:VolumenInvalido"), desc);

                var actual = await _music.ObtenerVolumenActualAsync(ctx.Guild.Id).ConfigureAwait(false);
                if (!MusicService.TryParseVolumen(nivel, actual, out var objetivo))
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Musica:VolumenInvalido"), desc);

                var aplicado = await _music.VolumenAsync(ctx.Guild.Id, objetivo).ConfigureAwait(false);
                return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Musica:Volumen", ("nivel", aplicado)), desc);
            });

        yield return new ToolDef(
            "music_shuffle",
            "Shuffle the music queue.",
            Esquema(),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                var desc = "/m shuffle";
                if (_music.Obtener(ctx.Guild.Id) is null)
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Musica:NoActivo"), desc);
                if (await _music.ValidarControlAsync(ctx.Guild, ctx.Miembro, _msg) is { Puede: false } control)
                    return new AiCommandResult(false, control.MensajeError!, desc);

                var exito = _music.AleorizarCola(ctx.Guild.Id);
                var texto = exito
                    ? _msg.Get(ctx.Guild.Id, "Musica:Aleatorizado")
                    : _msg.Get(ctx.Guild.Id, "Musica:ColaVacia");
                await _widget.RefrescarSiExisteAsync(ctx.Guild.Id, ctx.Canal).ConfigureAwait(false);
                return new AiCommandResult(exito, texto, desc);
            });

        yield return new ToolDef(
            "music_jump",
            "Jump to a specific position in the current song (e.g. 1:30 or 90).",
            Esquema(("position", "string", "Timestamp to jump to (e.g. 1:30 or 90).")),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                var desc = "/m jump";
                var posicionStr = ArgString(args, "position");
                if (string.IsNullOrWhiteSpace(posicionStr))
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Musica:TimestampInvalido"), desc);

                if (_music.Obtener(ctx.Guild.Id) is null)
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Musica:NoActivo"), desc);
                if (await _music.ValidarControlAsync(ctx.Guild, ctx.Miembro, _msg) is { Puede: false } control)
                    return new AiCommandResult(false, control.MensajeError!, desc);

                if (!MusicService.TryParseTimestamp(posicionStr, out var posicion))
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Musica:TimestampInvalido"), desc);

                var exito = await _music.SaltarAPosicionAsync(ctx.Guild.Id, posicion).ConfigureAwait(false);
                var texto = exito
                    ? _msg.Get(ctx.Guild.Id, "Musica:SaltadoA", ("posicion", MusicService.FormatearDuracion(posicion, false)))
                    : _msg.Get(ctx.Guild.Id, "Musica:ErrorSaltar");

                await _widget.RefrescarSiExisteAsync(ctx.Guild.Id, ctx.Canal).ConfigureAwait(false);
                return new AiCommandResult(exito, texto, desc);
            });

    }

    // ------------------------- configuración (ManageGuild) -------------------------

    private IEnumerable<ToolDef> CatalogoConfiguracion()
    {
        yield return new ToolDef(
            "welcome_set_channel",
            "Set the channel where new members are welcomed.",
            Esquema(("channel", "string", "The channel: mention (<#id>), ID, name, or \"current\".")),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                if (await ChequearPermisoGuild(ctx, Permissions.ManageGuild, "/welcome channel") is { } e) return e;
                var canal = ResolverCanalAsync(ctx, ArgString(args, "channel"), ChannelType.Text);
                if (canal is null)
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Bienvenida:VerNoConfigurado"), "/welcome channel");
                await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.WelcomeChannelId = canal.Id).ConfigureAwait(false);
                return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Bienvenida:ConfigCanalExito", ("canal", canal.Mention)), "/welcome channel");
            });

        yield return new ToolDef(
            "welcome_set_message",
            "Set the welcome message for new members ({usuario} and {servidor} placeholders).",
            Esquema(("message", "string", "The message. Placeholders: {usuario} {servidor}.")),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                if (await ChequearPermisoGuild(ctx, Permissions.ManageGuild, "/welcome message") is { } e) return e;
                var mensaje = ArgString(args, "message");
                if (string.IsNullOrWhiteSpace(mensaje) || mensaje.Length > 1900)
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Bienvenida:MensajeLargo"), "/welcome message");
                await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.WelcomeMessage = mensaje).ConfigureAwait(false);
                return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Bienvenida:ConfigMensajeExito", ("vista", mensaje)), "/welcome message");
            });

        yield return new ToolDef(
            "welcome_disable",
            "Disable welcome messages.",
            Esquema(),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                if (await ChequearPermisoGuild(ctx, Permissions.ManageGuild, "/welcome disable") is { } e) return e;
                var cfg = await _settings.GetAsync(ctx.Guild.Id).ConfigureAwait(false);
                if (cfg.WelcomeChannelId is null)
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Bienvenida:YaDesactivada"), "/welcome disable");
                await _settings.UpdateAsync(ctx.Guild.Id, c => c.WelcomeChannelId = null).ConfigureAwait(false);
                return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Bienvenida:ConfigDesactivada"), "/welcome disable");
            });

        yield return new ToolDef(
            "counting_set_channel",
            "Set the channel where the counting game happens.",
            Esquema(("channel", "string", "The text channel: mention (<#id>), ID, name, or \"current\".")),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                if (await ChequearPermisoGuild(ctx, Permissions.ManageGuild, "/counting channel") is { } e) return e;
                var canal = ResolverCanalAsync(ctx, ArgString(args, "channel"), ChannelType.Text);
                if (canal is null)
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Conteo:CanalDebeSerTexto"), "/counting channel");
                await _settings.UpdateCountingAsync(ctx.Guild.Id, cfg => cfg.ChannelId = canal.Id).ConfigureAwait(false);
                return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Conteo:CanalEstablecido", ("canal", canal.Mention)), "/counting channel");
            });

        yield return new ToolDef(
            "counting_disable",
            "Disable the counting game.",
            Esquema(),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                if (await ChequearPermisoGuild(ctx, Permissions.ManageGuild, "/counting disable") is { } e) return e;
                var cfg = await _settings.GetCountingAsync(ctx.Guild.Id).ConfigureAwait(false);
                if (cfg is null || cfg.ChannelId is null)
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Conteo:YaDesactivado"), "/counting disable");
                await _settings.UpdateCountingAsync(ctx.Guild.Id, c => c.ChannelId = null).ConfigureAwait(false);
                return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Conteo:Desactivado"), "/counting disable");
            });

        yield return new ToolDef(
            "counting_set_goal",
            "Set a numeric goal for the counting game.",
            Esquema(("number", "integer", "Goal number to reach (greater than 0).")),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                if (await ChequearPermisoGuild(ctx, Permissions.ManageGuild, "/counting goal") is { } e) return e;
                var numero = ArgLong(args, "number") ?? 0;
                if (numero <= 0)
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Conteo:ObjetivoInvalido"), "/counting goal");
                var cfg = await _settings.UpdateCountingAsync(ctx.Guild.Id, c => c.Goal = numero).ConfigureAwait(false);
                return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Conteo:ObjetivoEstablecido",
                    ("objetivo", CountingService.Formatear(numero, cfg.Base))), "/counting goal");
            });

        yield return new ToolDef(
            "counting_remove_goal",
            "Remove the counting game goal.",
            Esquema(),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                if (await ChequearPermisoGuild(ctx, Permissions.ManageGuild, "/counting goal-remove") is { } e) return e;
                await _settings.UpdateCountingAsync(ctx.Guild.Id, cfg => cfg.Goal = null).ConfigureAwait(false);
                return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Conteo:ObjetivoQuitado"), "/counting goal-remove");
            });

        yield return new ToolDef(
            "youtube_follow",
            "Subscribe the bot to a YouTube channel so it announces new videos in a Discord channel.",
            Esquema(
                ("channel", "string", "YouTube channel URL or @handle (e.g. https://www.youtube.com/@channel)."),
                ("notify", "string", "Discord text channel to announce in: mention (<#id>), ID, name or \"current\".")),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                if (await ChequearPermisoGuild(ctx, Permissions.ManageGuild, "/youtube follow") is { } e) return e;
                var notificar = ResolverCanalAsync(ctx, ArgString(args, "notify"), ChannelType.Text);
                if (notificar is null)
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Conteo:CanalDebeSerTexto"), "/youtube follow");

                var resuelto = await _yt.ResolverCanalAsync(ArgString(args, "channel") ?? "").ConfigureAwait(false);
                if (resuelto is null)
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "YouTube:ErrorResolver"), "/youtube follow");

                var (channelId, channelName) = resuelto.Value;
                var existente = await _settings.GetYouTubeAsync(ctx.Guild.Id).ConfigureAwait(false);
                var reemplazado = existente is not null;

                await _settings.UpdateYouTubeAsync(ctx.Guild.Id, sub =>
                {
                    sub.YTChannelId = channelId;
                    sub.YTChannelName = channelName;
                    sub.NotifyChannelId = notificar.Id;
                    sub.NotifyRoleId = null;
                    sub.LastVideoId = null;
                }).ConfigureAwait(false);

                var texto = reemplazado
                    ? _msg.Get(ctx.Guild.Id, "YouTube:SeguirReemplazado", ("canal", channelName), ("destino", notificar.Mention))
                    : _msg.Get(ctx.Guild.Id, "YouTube:SeguirExito", ("canal", channelName), ("destino", notificar.Mention));
                return new AiCommandResult(true, texto, "/youtube follow");
            });

        yield return new ToolDef(
            "youtube_unfollow",
            "Remove the server's YouTube subscription.",
            Esquema(),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                if (await ChequearPermisoGuild(ctx, Permissions.ManageGuild, "/youtube unfollow") is { } e) return e;
                var eliminado = await _settings.DeleteYouTubeAsync(ctx.Guild.Id).ConfigureAwait(false);
                return eliminado
                    ? new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "YouTube:Dejado"), "/youtube unfollow")
                    : new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "YouTube:NoSuscrito"), "/youtube unfollow");
            });

        yield return new ToolDef(
            "colors_install",
            "Install a color palette so users can pick their own name color.",
            Esquema(("palette", "string", "Palette name: \"normal\" or \"pastel\".")),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                if (await ChequearPermisoGuild(ctx, Permissions.ManageRoles, "/colors install") is { } e) return e;
                if (!ctx.Guild.CurrentMember.Permissions.HasPermission(Permissions.ManageRoles))
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Errores:SinPermisos"), "/colors install");

                var tipo = (ArgString(args, "palette") ?? "normal") == "pastel"
                    ? ColorService.PaletaType.Pastel
                    : ColorService.PaletaType.Normal;
                var (creados, _, total) = await _colors.InstalarAsync(ctx.Guild, tipo).ConfigureAwait(false);
                var paleta = tipo == ColorService.PaletaType.Pastel ? "pastel" : "normal";
                var texto = creados == 0
                    ? _msg.Get(ctx.Guild.Id, "Colores:InstalarRepetido", ("paleta", paleta))
                    : _msg.Get(ctx.Guild.Id, "Colores:Instalar", ("paleta", paleta), ("total", total));
                return new AiCommandResult(true, texto, "/colors install");
            });

        yield return new ToolDef(
            "colors_uninstall",
            "Remove the server's color palette.",
            Esquema(),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                if (await ChequearPermisoGuild(ctx, Permissions.ManageRoles, "/colors uninstall") is { } e) return e;
                if (!ctx.Guild.CurrentMember.Permissions.HasPermission(Permissions.ManageRoles))
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Errores:SinPermisos"), "/colors uninstall");

                var borrados = await _colors.DesinstalarAsync(ctx.Guild).ConfigureAwait(false);
                return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Colores:Desinstalar", ("borrados", borrados)), "/colors uninstall");
            });

        yield return new ToolDef(
            "channel_create",
            "Create a text or voice channel.",
            Esquema(
                ("name", "string", "Channel name."),
                ("type", "string", "\"voice\" or \"text\"."),
                ("category", "string", "Optional category: mention (<#id>), ID or name.")),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                if (await ChequearPermisoGuild(ctx, Permissions.ManageChannels, "/channel create") is { } e) return e;
                if (!ctx.Guild.CurrentMember.Permissions.HasPermission(Permissions.ManageChannels))
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Errores:SinPermisos"), "/channel create");

                var nombre = ArgString(args, "name");
                if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 100)
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Chat:ErrorEjecucion"), "/channel create");

                var categoria = ResolverCanalAsync(ctx, ArgString(args, "category"), ChannelType.Category);
                var tipo = (ArgString(args, "type") ?? "text").Trim().ToLowerInvariant();

                DiscordChannel canal;
                if (tipo == "voice")
                    canal = categoria is null
                        ? await ctx.Guild.CreateVoiceChannelAsync(nombre, reason: "Creado desde el chat").ConfigureAwait(false)
                        : await ctx.Guild.CreateVoiceChannelAsync(nombre, categoria, reason: "Creado desde el chat").ConfigureAwait(false);
                else
                    canal = categoria is null
                        ? await ctx.Guild.CreateTextChannelAsync(nombre, reason: "Creado desde el chat").ConfigureAwait(false)
                        : await ctx.Guild.CreateTextChannelAsync(nombre, categoria, reason: "Creado desde el chat").ConfigureAwait(false);

                return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Voces:Creado", ("canal", canal.Mention)), "/channel create");
            });

        yield return new ToolDef(
            "logchannel_set",
            "Set the channel where moderation incidents are announced.",
            Esquema(("channel", "string", "The text channel: mention (<#id>), ID, name, or \"current\".")),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                if (await ChequearPermisoGuild(ctx, Permissions.ManageGuild, "/log-channel") is { } e) return e;
                var canal = ResolverCanalAsync(ctx, ArgString(args, "channel"), ChannelType.Text);
                if (canal is null)
                    return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Config:VerNoConfigurado"), "/log-channel");
                await _settings.UpdateAsync(ctx.Guild.Id, cfg => cfg.ModLogChannelId = canal.Id).ConfigureAwait(false);
                return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Config:CanalLogsEstablecido", ("canal", canal.Mention)), "/log-channel");
            });
    }

    // ------------------------- estado (solo lectura) -------------------------

    private IEnumerable<ToolDef> CatalogoEstado()
    {
        yield return new ToolDef(
            "get_server_state",
            "Read-only: get the current server state (music volume, now playing, locked channels, counting game, welcome, DJ role, AI toggles, language). Use this to know current values before acting.",
            Esquema(),
            Destructivo: false,
            DescripcionComando: null,
            Ejecutar: async (ctx, args) =>
            {
                var s = await _settings.GetSnapshotAsync(ctx.Guild.Id).ConfigureAwait(false);

                var lineas = new List<string>
                {
                    $"Language: {s.Language}",
                    $"Music volume: {s.Music.Volume ?? 100}",
                    $"DJ role: {(s.Music.DjRoleId is { } dj ? $"<@&{dj}>" : "none")}",
                    $"Welcome: {(s.Welcome.Enabled ? $"on in <#{s.Welcome.ChannelId}>" : "off")}",
                    $"Join-to-create hub: {(s.Voice.HubChannelId is { } hub ? $"<#{hub}>" : "off")}",
                    $"Locked channels: {(s.BlockedChannels.Count > 0 ? string.Join(", ", s.BlockedChannels.Select(id => $"<#{id}>")) : "none")}",
                    $"AI chat: {(s.Ai.ChatEnabled ? "on" : "off")}, mentions: {(s.Ai.MentionsEnabled ? "on" : "off")}, spontaneous: {(s.Ai.SpontaneousEnabled ? "on" : "off")}, web search: {(s.Ai.WebSearchEnabled ? "on" : "off")}, commands: {(s.Ai.CommandsEnabled ? "on" : "off")}",
                    $"Downloads: {(s.Downloads.Enabled ? "on" : "off")}"
                };

                if (s.Counting is { } c)
                    lineas.Add(c.Enabled
                        ? $"Counting: on in <#{c.ChannelId}>, base {c.Base}, value {c.CurrentValue}, record {c.CurrentRecord}, goal {c.Goal?.ToString() ?? "none"}"
                        : "Counting: off");
                else
                    lineas.Add("Counting: never configured");

                if (s.YouTube is { } yt)
                    lineas.Add($"YouTube: subscribed to {yt.ChannelName} (announces in <#{yt.NotifyChannelId}>)");

                if (_music.Obtener(ctx.Guild.Id) is { } player)
                {
                    var actual = player.CurrentTrack;
                    lineas.Add(actual is null
                        ? "Music: player connected, nothing playing"
                        : $"Music: playing \"{actual.Title}\" by {actual.Author}, queue: {player.Queue.Count}");
                }
                else
                {
                    lineas.Add("Music: not connected to any voice channel");
                }

                return new AiCommandResult(true, string.Join("\n", lineas), "/config");
            });
    }
}
