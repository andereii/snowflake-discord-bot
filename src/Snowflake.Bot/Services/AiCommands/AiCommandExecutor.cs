using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Snowflake.Bot.Data;
using Snowflake.Bot.Data.Entities;
using Snowflake.Bot.Services.Calculators;
using Snowflake.Bot.Services.Settings;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Services.AiCommands;

/// <summary>
/// Contexto de ejecución de un comando por IA: el usuario que lo pide, el
/// servidor y el canal donde se habla. No depende de InteractionContext para
/// que funcione igual desde /talk y desde menciones.
/// </summary>
public sealed record AiCommandContext(
    DiscordClient Client,
    DiscordGuild Guild,
    DiscordChannel Canal,
    DiscordMember Miembro);

/// <summary>Resultado de ejecutar una tool: texto localizado (para embed) + descripción legible del comando.</summary>
public sealed record AiCommandResult(bool Exitoso, string Texto, string Descripcion);

/// <summary>
/// Resultado del intento de ejecución:
/// - Resultado no nulo → comando ya ejecutado (mostrar embed con su output).
/// - Destructivo → el comando es destructivo y requiere confirmación con
///   botones; <see cref="DescripcionComando"/> es lo que se muestra ("/ban @usuario").
/// - Error → hubo un fallo inesperado.
/// </summary>
public sealed record AiToolExecution
{
    public bool Destructivo { get; init; }
    public bool Error { get; init; }
    public string DescripcionComando { get; init; } = "";
    public AiCommandResult? Resultado { get; init; }
}

/// <summary>Definición declarativa de una tool (comando) que el modelo puede pedir.</summary>
public sealed record ToolDef(
    string Nombre,
    string Descripcion,
    JsonNode Esquema,
    bool Destructivo,
    Func<AiCommandContext, JsonObject, Task<string>>? DescripcionComando,
    Func<AiCommandContext, JsonObject, Task<AiCommandResult>> Ejecutar);

/// <summary>
/// Catálogo de comandos del bot ejecutables desde el chat. El modelo SOLO
/// propone el comando y sus argumentos; aquí se validan permisos reales
/// (iguales a los slash commands) y se ejecuta con los mismos servicios.
/// Los comandos marcados como destructivos no se ejecutan: devuelven
/// <see cref="AiToolExecution.Destructivo"/> para que el bot pida
/// confirmación con botones al usuario que lo solicitó.
/// </summary>
public sealed partial class AiCommandExecutor
{
    private static readonly Regex MencionRegex = new(@"<@!?(\d+)>", RegexOptions.Compiled);
    private static readonly Regex CanalRegex = new(@"<#(\d+)>", RegexOptions.Compiled);
    private static readonly Regex RolRegex = new(@"<@&(\d+)>", RegexOptions.Compiled);

    private readonly DiscordClient _client;
    private readonly GuildSettingsService _settings;
    private readonly MessagesService _msg;
    private readonly ModerationLogService _modLog;
    private readonly ChannelLockService _locks;
    private readonly MusicService _music;
    private readonly MusicWidgetService _widget;
    private readonly ColorService _colors;
    private readonly YouTubeNotifyService _yt;
    private readonly IDbContextFactory<BotDbContext> _dbFactory;
    private readonly ILogger<AiCommandExecutor> _logger;

    private readonly Dictionary<string, ToolDef> _tools;

    public AiCommandExecutor(
        DiscordClient client,
        GuildSettingsService settings,
        MessagesService msg,
        ModerationLogService modLog,
        ChannelLockService locks,
        MusicService music,
        MusicWidgetService widget,
        ColorService colors,
        YouTubeNotifyService yt,
        IDbContextFactory<BotDbContext> dbFactory,
        ILogger<AiCommandExecutor> logger)
    {
        _client = client;
        _settings = settings;
        _msg = msg;
        _modLog = modLog;
        _locks = locks;
        _music = music;
        _widget = widget;
        _colors = colors;
        _yt = yt;
        _dbFactory = dbFactory;
        _logger = logger;

        _tools = ConstruirCatalogo();
    }

    /// <summary>Todas las tools expuestas al modelo.</summary>
    public IReadOnlyCollection<ToolDef> Herramientas => _tools.Values;

    public async Task<AiToolExecution> EjecutarAsync(
        AiCommandContext ctx, string nombre, JsonObject? args, bool esConfirmacion = false)
    {
        args ??= new JsonObject();

        if (!_tools.TryGetValue(nombre, out var tool))
        {
            _logger.LogWarning("El modelo pidió una tool desconocida: {Tool}", nombre);
            return new AiToolExecution
            {
                Error = true,
                Resultado = new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Chat:ErrorEjecucion"), nombre)
            };
        }

        if (tool.Destructivo && !esConfirmacion)
        {
            var desc = tool.DescripcionComando is not null
                ? await tool.DescripcionComando(ctx, args).ConfigureAwait(false)
                : nombre;
            return new AiToolExecution { Destructivo = true, DescripcionComando = desc };
        }

        try
        {
            var resultado = await tool.Ejecutar(ctx, args).ConfigureAwait(false);
            return new AiToolExecution { Resultado = resultado };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ejecutando la tool de IA {Tool}", nombre);
            return new AiToolExecution
            {
                Error = true,
                Resultado = new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Chat:ErrorEjecucion"), nombre)
            };
        }
    }

    // ------------------------- helpers -------------------------

    /// <summary>Construye un esquema JSON de parámetros con las propiedades indicadas.</summary>
    private static JsonNode Esquema(params (string Prop, string Tipo, string Desc)[] props)
    {
        var properties = new JsonObject();
        foreach (var (prop, tipo, desc) in props)
            properties[prop] = new JsonObject { ["type"] = tipo, ["description"] = desc };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
    }

    /// <summary>Lee un campo string de los argumentos (null si no viene o está vacío).</summary>
    private static string? ArgString(JsonObject args, string clave)
        => args[clave]?.GetValue<string>() is { } v && v.Trim().Length > 0 ? v.Trim() : null;

    private static long? ArgLong(JsonObject args, string clave)
    {
        if (args[clave] is not { } n) return null;
        try { return n.GetValue<long>(); } catch { return null; }
    }

    /// <summary>Resuelve "usuario" (mención, ID o nombre/nick) a un miembro del servidor.</summary>
    private async Task<DiscordMember?> ResolverUsuarioAsync(AiCommandContext ctx, string? usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario)) return null;
        usuario = usuario.Trim();

        var m = MencionRegex.Match(usuario);
        if (m.Success && ulong.TryParse(m.Groups[1].Value, out var idMencion))
            return await ObtenerMiembroAsync(ctx, idMencion);

        if (ulong.TryParse(usuario, out var id))
            return await ObtenerMiembroAsync(ctx, id);

        foreach (var miembro in ctx.Guild.Members.Values)
        {
            if (miembro.Username.Equals(usuario, StringComparison.OrdinalIgnoreCase)
                || miembro.DisplayName.Equals(usuario, StringComparison.OrdinalIgnoreCase))
                return miembro;
        }
        foreach (var miembro in ctx.Guild.Members.Values)
        {
            if (miembro.DisplayName.Contains(usuario, StringComparison.OrdinalIgnoreCase)
                || miembro.Username.Contains(usuario, StringComparison.OrdinalIgnoreCase))
                return miembro;
        }
        return null;
    }

    private static async Task<DiscordMember?> ObtenerMiembroAsync(AiCommandContext ctx, ulong id)
    {
        try { return await ctx.Guild.GetMemberAsync(id).ConfigureAwait(false); }
        catch { return null; }
    }

    /// <summary>Resuelve "canal" (mención, ID, "current"/"here" o nombre) a un canal del servidor.</summary>
    private DiscordChannel? ResolverCanalAsync(AiCommandContext ctx, string? canal, ChannelType? tipo = null)
    {
        if (string.IsNullOrWhiteSpace(canal)) return null;
        canal = canal.Trim();

        if (canal is "current" or "here" or "this channel" or "este canal" or "aqui" or "aquí")
            return ctx.Canal;

        var m = CanalRegex.Match(canal);
        if (m.Success && ulong.TryParse(m.Groups[1].Value, out var id))
            return ctx.Guild.GetChannel(id);

        if (ulong.TryParse(canal, out var id2))
            return ctx.Guild.GetChannel(id2);

        foreach (var c in ctx.Guild.Channels.Values)
        {
            if (tipo is not null && c.Type != tipo.Value) continue;
            if (c.Name.Equals(canal, StringComparison.OrdinalIgnoreCase)) return c;
        }
        foreach (var c in ctx.Guild.Channels.Values)
        {
            if (tipo is not null && c.Type != tipo.Value) continue;
            if (c.Name.Contains(canal, StringComparison.OrdinalIgnoreCase)) return c;
        }
        return null;
    }

    /// <summary>Resuelve "rol" (mención, ID o nombre) a un rol del servidor.</summary>
    private DiscordRole? ResolverRol(AiCommandContext ctx, string? rolNombreOId)
    {
        if (string.IsNullOrWhiteSpace(rolNombreOId)) return null;
        rolNombreOId = rolNombreOId.Trim();

        var m = RolRegex.Match(rolNombreOId);
        if (m.Success && ulong.TryParse(m.Groups[1].Value, out var idMencion))
            return ctx.Guild.GetRole(idMencion);

        if (ulong.TryParse(rolNombreOId, out var id))
            return ctx.Guild.GetRole(id);

        foreach (var r in ctx.Guild.Roles.Values)
        {
            if (r.Name.Equals(rolNombreOId, StringComparison.OrdinalIgnoreCase))
                return r;
        }

        foreach (var r in ctx.Guild.Roles.Values)
        {
            if (r.Name.Contains(rolNombreOId, StringComparison.OrdinalIgnoreCase))
                return r;
        }

        return null;
    }

    /// <summary>Chequeo de permiso a nivel de guild (equivalente a [SlashRequirePermissions]).</summary>
    private Task<AiCommandResult?> ChequearPermisoGuild(AiCommandContext ctx, Permissions permiso, string descripcion)
        => Task.FromResult(ctx.Miembro.Permissions.HasPermission(permiso)
            ? null
            : new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Errores:SinPermisos"), descripcion));

    /// <summary>Validación de objetivo de moderación: no a sí mismo, no al bot, no al owner, jerarquía.</summary>
    private AiCommandResult? ValidarObjetivo(AiCommandContext ctx, DiscordMember objetivo, string descripcion)
    {
        if (objetivo.Id == ctx.Miembro.Id)
            return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Moderacion:Errores:MismoUsuario"), descripcion);
        if (objetivo.Id == ctx.Client.CurrentUser.Id)
            return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Moderacion:Errores:AlBot"), descripcion);
        if (objetivo.IsOwner)
            return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Moderacion:Errores:EsOwner"), descripcion);
        if (objetivo.Hierarchy >= ctx.Guild.CurrentMember.Hierarchy)
            return new AiCommandResult(false,
                _msg.Get(ctx.Guild.Id, "Moderacion:Errores:Jerarquia", ("usuario", objetivo.Username)), descripcion);
        return null;
    }

    // ------------------------- moderación (destructivas) -------------------------

    private ToolDef ToolBan() => new(
        "ban_user",
        "Ban a member from the server. Destructive: the bot will ask the requesting user for authorization before executing. Use the user's mention (<@id>), their ID or their exact username.",
        Esquema(("user", "string", "The user to ban: mention (<@id>), ID or exact username."),
                ("reason", "string", "Optional reason."),
                ("delete_days", "integer", "Days of their messages to delete (0-7).")),
        Destructivo: true,
        DescripcionComando: async (ctx, args) =>
        {
            var u = await ResolverUsuarioAsync(ctx, ArgString(args, "user")).ConfigureAwait(false);
            return $"/ban {(u is null ? ArgString(args, "user") : "@" + u.Username)}";
        },
        Ejecutar: async (ctx, args) =>
        {
            var desc = "/ban";
            var usuario = ArgString(args, "user");
            var motivo = ArgString(args, "reason") ?? _msg.Get(ctx.Guild.Id, "Moderacion:MotivoPorDefecto");
            var dias = (int)Math.Clamp(ArgLong(args, "delete_days") ?? 0, 0, 7);

            if (await ChequearPermisoGuild(ctx, Permissions.BanMembers, desc) is { } error) return error;

            ulong objetivoId;
            string objetivoNombre;

            var miembro = await ResolverUsuarioAsync(ctx, usuario).ConfigureAwait(false);
            if (miembro is not null)
            {
                if (ValidarObjetivo(ctx, miembro, desc) is { } invalido) return invalido;
                await _modLog.AvisarPrivadoAsync(miembro, ctx.Guild.Name,
                    _msg.Get(ctx.Guild.Id, "Moderacion:Dm:Acciones:Veto"), motivo).ConfigureAwait(false);
                objetivoId = miembro.Id;
                objetivoNombre = miembro.Username;
            }
            else
            {
                // El veto funciona aunque el usuario ya no esté en el servidor,
                // pero entonces solo vale un ID (mención o número).
                var m = MencionRegex.Match(usuario ?? "");
                if (m.Success && ulong.TryParse(m.Groups[1].Value, out var idMencion))
                {
                    objetivoId = idMencion;
                    objetivoNombre = usuario!;
                }
                else if (ulong.TryParse(usuario, out var idDirecto))
                {
                    objetivoId = idDirecto;
                    objetivoNombre = usuario!;
                }
                else
                {
                    return new AiCommandResult(false,
                        _msg.Get(ctx.Guild.Id, "Moderacion:Errores:NoEnServidor", ("usuario", usuario)), desc);
                }
            }

            await ctx.Guild.BanMemberAsync(objetivoId, dias, motivo).ConfigureAwait(false);

            var incidente = await _modLog.RegistrarAsync(
                ctx.Guild.Id, objetivoId, objetivoNombre, ctx.Miembro, IncidentType.Veto, motivo).ConfigureAwait(false);
            await _modLog.AnunciarAsync(ctx.Guild, incidente).ConfigureAwait(false);

            return new AiCommandResult(true,
                _msg.Get(ctx.Guild.Id, "Moderacion:Exito:Veto", ("usuario", objetivoNombre)), desc);
        });

    private ToolDef ToolKick() => new(
        "kick_user",
        "Kick a member from the server. Destructive: the bot will ask the requesting user for authorization before executing.",
        Esquema(("user", "string", "The user to kick: mention (<@id>), ID or exact username."),
                ("reason", "string", "Optional reason.")),
        Destructivo: true,
        DescripcionComando: async (ctx, args) =>
        {
            var u = await ResolverUsuarioAsync(ctx, ArgString(args, "user")).ConfigureAwait(false);
            return $"/kick {(u is null ? ArgString(args, "user") : "@" + u.Username)}";
        },
        Ejecutar: async (ctx, args) =>
        {
            var desc = "/kick";
            var usuario = ArgString(args, "user");
            var motivo = ArgString(args, "reason") ?? _msg.Get(ctx.Guild.Id, "Moderacion:MotivoPorDefecto");

            if (await ChequearPermisoGuild(ctx, Permissions.KickMembers, desc) is { } error) return error;

            var miembro = await ResolverUsuarioAsync(ctx, usuario).ConfigureAwait(false);
            if (miembro is null)
                return new AiCommandResult(false,
                    _msg.Get(ctx.Guild.Id, "Moderacion:Errores:NoEnServidor", ("usuario", usuario)), desc);
            if (ValidarObjetivo(ctx, miembro, desc) is { } invalido) return invalido;

            await _modLog.AvisarPrivadoAsync(miembro, ctx.Guild.Name,
                _msg.Get(ctx.Guild.Id, "Moderacion:Dm:Acciones:Expulsion"), motivo).ConfigureAwait(false);
            await miembro.RemoveAsync(motivo).ConfigureAwait(false);

            var incidente = await _modLog.RegistrarAsync(ctx.Guild.Id, miembro, ctx.Miembro, IncidentType.Expulsion, motivo).ConfigureAwait(false);
            await _modLog.AnunciarAsync(ctx.Guild, incidente).ConfigureAwait(false);

            return new AiCommandResult(true,
                _msg.Get(ctx.Guild.Id, "Moderacion:Exito:Expulsion", ("usuario", miembro.Username)), desc);
        });

    private ToolDef ToolTimeout() => new(
        "timeout_user",
        "Time out (mute) a member for a duration like 10m, 2h or 7d. Destructive: the bot will ask the requesting user for authorization before executing.",
        Esquema(("user", "string", "The user to time out: mention (<@id>), ID or exact username."),
                ("duration", "string", "Duration like 30s, 10m, 2h or 7d (max 28 days)."),
                ("reason", "string", "Optional reason.")),
        Destructivo: true,
        DescripcionComando: async (ctx, args) =>
        {
            var u = await ResolverUsuarioAsync(ctx, ArgString(args, "user")).ConfigureAwait(false);
            return $"/timeout {(u is null ? ArgString(args, "user") : "@" + u.Username)} {ArgString(args, "duration") ?? ""}".TrimEnd();
        },
        Ejecutar: async (ctx, args) =>
        {
            var desc = "/timeout";
            var usuario = ArgString(args, "user");
            var motivo = ArgString(args, "reason") ?? _msg.Get(ctx.Guild.Id, "Moderacion:MotivoPorDefecto");

            if (await ChequearPermisoGuild(ctx, Permissions.ModerateMembers, desc) is { } error) return error;

            if (!DurationParser.TryParse(ArgString(args, "duration"), out var tiempo) || tiempo <= TimeSpan.Zero)
                return new AiCommandResult(false,
                    _msg.Get(ctx.Guild.Id, "Moderacion:Errores:DuracionInvalida"), desc);
            if (tiempo > TimeSpan.FromDays(28))
                return new AiCommandResult(false,
                    _msg.Get(ctx.Guild.Id, "Moderacion:Errores:DuracionMaxima"), desc);

            var miembro = await ResolverUsuarioAsync(ctx, usuario).ConfigureAwait(false);
            if (miembro is null)
                return new AiCommandResult(false,
                    _msg.Get(ctx.Guild.Id, "Moderacion:Errores:NoEnServidor", ("usuario", usuario)), desc);
            if (ValidarObjetivo(ctx, miembro, desc) is { } invalido) return invalido;

            await _modLog.AvisarPrivadoAsync(miembro, ctx.Guild.Name,
                _msg.Get(ctx.Guild.Id, "Moderacion:Dm:Acciones:Aislamiento", ("duracion", DurationParser.Format(tiempo, _msg.Locale(ctx.Guild.Id)))), motivo).ConfigureAwait(false);
            await miembro.TimeoutAsync(DateTimeOffset.UtcNow + tiempo, motivo).ConfigureAwait(false);

            var incidente = await _modLog.RegistrarAsync(ctx.Guild.Id, miembro, ctx.Miembro, IncidentType.Aislamiento, motivo, tiempo).ConfigureAwait(false);
            await _modLog.AnunciarAsync(ctx.Guild, incidente).ConfigureAwait(false);

            return new AiCommandResult(true,
                _msg.Get(ctx.Guild.Id, "Moderacion:Exito:Aislamiento",
                    ("usuario", miembro.Username),
                    ("duracion", DurationParser.Format(tiempo, _msg.Locale(ctx.Guild.Id)))), desc);
        });

    private ToolDef ToolUntimeout() => new(
        "untimeout_user",
        "Remove a member's timeout.",
        Esquema(("user", "string", "The user: mention (<@id>), ID or exact username."),
                ("reason", "string", "Optional reason.")),
        Destructivo: false,
        DescripcionComando: null,
        Ejecutar: async (ctx, args) =>
        {
            var desc = "/untimeout";
            var usuario = ArgString(args, "user");
            var motivo = ArgString(args, "reason") ?? _msg.Get(ctx.Guild.Id, "Moderacion:MotivoPorDefecto");

            if (await ChequearPermisoGuild(ctx, Permissions.ModerateMembers, desc) is { } error) return error;

            var miembro = await ResolverUsuarioAsync(ctx, usuario).ConfigureAwait(false);
            if (miembro is null)
                return new AiCommandResult(false,
                    _msg.Get(ctx.Guild.Id, "Moderacion:Errores:NoEnServidor", ("usuario", usuario)), desc);
            if (ValidarObjetivo(ctx, miembro, desc) is { } invalido) return invalido;

            await miembro.TimeoutAsync(null, motivo).ConfigureAwait(false);

            var incidente = await _modLog.RegistrarAsync(ctx.Guild.Id, miembro, ctx.Miembro, IncidentType.FinAislamiento, motivo).ConfigureAwait(false);
            await _modLog.AnunciarAsync(ctx.Guild, incidente).ConfigureAwait(false);

            return new AiCommandResult(true,
                _msg.Get(ctx.Guild.Id, "Moderacion:Exito:FinAislamiento", ("usuario", miembro.Username)), desc);
        });

    private ToolDef ToolWarn() => new(
        "warn_user",
        "Record a warning for a member. Destructive: the bot will ask the requesting user for authorization before executing.",
        Esquema(("user", "string", "The user: mention (<@id>), ID or exact username."),
                ("reason", "string", "Reason for the warning.")),
        Destructivo: true,
        DescripcionComando: async (ctx, args) =>
        {
            var u = await ResolverUsuarioAsync(ctx, ArgString(args, "user")).ConfigureAwait(false);
            return $"/warn {(u is null ? ArgString(args, "user") : "@" + u.Username)}";
        },
        Ejecutar: async (ctx, args) =>
        {
            var desc = "/warn";
            var usuario = ArgString(args, "user");
            var motivo = ArgString(args, "reason") ?? _msg.Get(ctx.Guild.Id, "Moderacion:MotivoPorDefecto");

            if (await ChequearPermisoGuild(ctx, Permissions.ModerateMembers, desc) is { } error) return error;

            var miembro = await ResolverUsuarioAsync(ctx, usuario).ConfigureAwait(false);
            if (miembro is null)
                return new AiCommandResult(false,
                    _msg.Get(ctx.Guild.Id, "Moderacion:Errores:NoEnServidor", ("usuario", usuario)), desc);
            if (ValidarObjetivo(ctx, miembro, desc) is { } invalido) return invalido;

            await _modLog.AvisarPrivadoAsync(miembro, ctx.Guild.Name,
                _msg.Get(ctx.Guild.Id, "Moderacion:Dm:Acciones:Advertencia"), motivo).ConfigureAwait(false);

            var incidente = await _modLog.RegistrarAsync(ctx.Guild.Id, miembro, ctx.Miembro, IncidentType.Advertencia, motivo).ConfigureAwait(false);
            await _modLog.AnunciarAsync(ctx.Guild, incidente).ConfigureAwait(false);

            return new AiCommandResult(true,
                _msg.Get(ctx.Guild.Id, "Moderacion:Exito:Advertencia", ("usuario", miembro.Username)), desc);
        });

    private ToolDef ToolHistory() => new(
        "get_user_history",
        "Show a user's recent moderation incidents (read-only).",
        Esquema(("user", "string", "The user: mention (<@id>), ID or exact username.")),
        Destructivo: false,
        DescripcionComando: null,
        Ejecutar: async (ctx, args) =>
        {
            if (await ChequearPermisoGuild(ctx, Permissions.ModerateMembers, "/history") is { } error) return error;

            var usuario = ArgString(args, "user");
            var miembro = await ResolverUsuarioAsync(ctx, usuario).ConfigureAwait(false);

            await using var db = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
            var consulta = db.Incidents.Where(i => i.GuildId == ctx.Guild.Id);
            if (miembro is not null)
                consulta = consulta.Where(i => i.TargetUserId == miembro.Id);

            var ultimos = await consulta.OrderByDescending(i => i.Id).Take(10).ToListAsync().ConfigureAwait(false);

            if (ultimos.Count == 0)
                return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Moderacion:Historial:Vacio"), "/history");

            var lineas = new List<string>();
            foreach (var i in ultimos)
            {
                var duracion = i.Duration is { } d ? $" · {DurationParser.Format(d, _msg.Locale(ctx.Guild.Id))}" : "";
                lineas.Add(_msg.Get(ctx.Guild.Id, "Moderacion:Historial:Linea",
                    ("usuario", $"<@{i.TargetUserId}>"),
                    ("moderador", $"<@{i.ModeratorId}>"),
                    ("motivo", i.Reason)) + duracion);
            }
            return new AiCommandResult(true, string.Join("\n", lineas), "/history");
        });

    // ------------------------- canales: lock/unlock/clear -------------------------

    private ToolDef ToolLock() => new(
        "lock_channel",
        "Lock a channel so nobody can talk in it (or connect, if it's a voice channel).",
        Esquema(("channel", "string", "The channel: mention (<#id>), ID, name, or \"current\" for the chat channel."),
                ("reason", "string", "Optional reason.")),
        Destructivo: false,
        DescripcionComando: null,
        Ejecutar: async (ctx, args) =>
        {
            var desc = "/lock";
            var canal = ResolverCanalAsync(ctx, ArgString(args, "channel")) ?? ctx.Canal;
            var motivo = ArgString(args, "reason") ?? _msg.Get(ctx.Guild.Id, "Moderacion:MotivoPorDefecto");

            if (canal.Type is not (ChannelType.Text or ChannelType.News or ChannelType.GuildForum
                or ChannelType.Voice or ChannelType.Stage))
                return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Bloqueo:CanalInvalido"), desc);

            if (!canal.PermissionsFor(ctx.Miembro).HasPermission(Permissions.ManageChannels))
                return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Bloqueo:SinPermisosCanal", ("canal", canal.Mention)), desc);
            if (!canal.PermissionsFor(ctx.Guild.CurrentMember).HasPermission(Permissions.ManageRoles))
                return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Bloqueo:SinPermisosBotCanal", ("canal", canal.Mention)), desc);

            var aplicado = await _locks.BloquearAsync(canal, motivo).ConfigureAwait(false);
            return new AiCommandResult(aplicado,
                aplicado
                    ? _msg.Get(ctx.Guild.Id, "Bloqueo:Bloqueado", ("canal", canal.Mention))
                    : _msg.Get(ctx.Guild.Id, "Bloqueo:YaBloqueado", ("canal", canal.Mention)),
                desc);
        });

    private ToolDef ToolUnlock() => new(
        "unlock_channel",
        "Unlock a channel previously locked with /lock.",
        Esquema(("channel", "string", "The channel: mention (<#id>), ID, name, or \"current\" for the chat channel."),
                ("reason", "string", "Optional reason.")),
        Destructivo: false,
        DescripcionComando: null,
        Ejecutar: async (ctx, args) =>
        {
            var desc = "/unlock";
            var canal = ResolverCanalAsync(ctx, ArgString(args, "channel")) ?? ctx.Canal;
            var motivo = ArgString(args, "reason") ?? _msg.Get(ctx.Guild.Id, "Moderacion:MotivoPorDefecto");

            if (canal.Type is not (ChannelType.Text or ChannelType.News or ChannelType.GuildForum
                or ChannelType.Voice or ChannelType.Stage))
                return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Bloqueo:CanalInvalido"), desc);

            if (!canal.PermissionsFor(ctx.Miembro).HasPermission(Permissions.ManageChannels))
                return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Bloqueo:SinPermisosCanal", ("canal", canal.Mention)), desc);
            if (!canal.PermissionsFor(ctx.Guild.CurrentMember).HasPermission(Permissions.ManageRoles))
                return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Bloqueo:SinPermisosBotCanal", ("canal", canal.Mention)), desc);

            var aplicado = await _locks.DesbloquearAsync(canal, motivo).ConfigureAwait(false);
            return new AiCommandResult(aplicado,
                aplicado
                    ? _msg.Get(ctx.Guild.Id, "Bloqueo:Desbloqueado", ("canal", canal.Mention))
                    : _msg.Get(ctx.Guild.Id, "Bloqueo:NoBloqueado", ("canal", canal.Mention)),
                desc);
        });

    private ToolDef ToolClear() => new(
        "clear_messages",
        "Delete recent messages in a channel (bulk up to 100). Destructive: the bot will ask the requesting user for authorization before executing.",
        Esquema(("channel", "string", "The channel: mention (<#id>), ID, name, or \"current\" for the chat channel."),
                ("amount", "integer", "How many messages to delete (1-100).")),
        Destructivo: true,
        DescripcionComando: async (ctx, args) =>
        {
            var canal = ResolverCanalAsync(ctx, ArgString(args, "channel")) ?? ctx.Canal;
            var n = (int)Math.Clamp(ArgLong(args, "amount") ?? 10, 1, 100);
            return $"/clear {n} #{canal.Name}";
        },
        Ejecutar: async (ctx, args) =>
        {
            var desc = "/clear";
            var canal = ResolverCanalAsync(ctx, ArgString(args, "channel")) ?? ctx.Canal;
            var cantidad = (int)Math.Clamp(ArgLong(args, "amount") ?? 10, 1, 100);

            if (canal.Type != ChannelType.Text)
                return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Limpiar:CanalDebeSerTexto"), desc);
            if (!canal.PermissionsFor(ctx.Miembro).HasPermission(Permissions.ManageMessages))
                return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Limpiar:SinPermisosCanal", ("canal", canal.Mention)), desc);
            if (!canal.PermissionsFor(ctx.Guild.CurrentMember).HasPermission(Permissions.ManageMessages))
                return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Limpiar:SinPermisosBotCanal", ("canal", canal.Mention)), desc);

            var mensajes = await canal.GetMessagesAsync(cantidad).ConfigureAwait(false);
            var borrables = (mensajes ?? [])
                .Where(m => (DateTimeOffset.UtcNow - m.CreationTimestamp) < TimeSpan.FromDays(14))
                .ToList();

            var borrados = 0;
            if (borrables.Count == 1)
            {
                try { await canal.DeleteMessageAsync(borrables[0], "/clear").ConfigureAwait(false); borrados++; } catch { }
            }
            else if (borrables.Count > 1)
            {
                try { await canal.DeleteMessagesAsync(borrables, "/clear").ConfigureAwait(false); borrados += borrables.Count; } catch { }
            }

            var viejos = (mensajes ?? []).Count(m => (DateTimeOffset.UtcNow - m.CreationTimestamp) >= TimeSpan.FromDays(14));
            foreach (var viejo in (mensajes ?? []).Where(m => (DateTimeOffset.UtcNow - m.CreationTimestamp) >= TimeSpan.FromDays(14)))
            {
                try { await canal.DeleteMessageAsync(viejo, "/clear").ConfigureAwait(false); borrados++; } catch { }
                await Task.Delay(600).ConfigureAwait(false);
            }

            var texto = borrados == 0
                ? _msg.Get(ctx.Guild.Id, "Limpiar:SinMensajes", ("canal", canal.Mention))
                : viejos > 0
                    ? _msg.Get(ctx.Guild.Id, "Limpiar:ExitoExcluido", ("n", borrados), ("canal", canal.Mention), ("excluidos", viejos))
                    : _msg.Get(ctx.Guild.Id, "Limpiar:Exito", ("n", borrados), ("canal", canal.Mention));

            return new AiCommandResult(borrados > 0, texto, desc);
        });

    private ToolDef ToolRoleAdd() => new(
        "role_add",
        "Add a role to a server member. Requires ManageRoles permission on both the user and the bot.",
        Esquema(("user", "string", "The user to receive the role: mention (<@id>), ID or username."),
                ("role", "string", "The role name, mention (<@&id>) or ID to assign.")),
        Destructivo: false,
        DescripcionComando: null,
        Ejecutar: async (ctx, args) =>
        {
            var desc = "/role add";
            if (await ChequearPermisoGuild(ctx, Permissions.ManageRoles, desc) is { } error) return error;

            var usuarioStr = ArgString(args, "user");
            var rolStr = ArgString(args, "role");

            var miembro = await ResolverUsuarioAsync(ctx, usuarioStr).ConfigureAwait(false);
            if (miembro is null)
                return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Moderacion:NoMiembro"), desc);

            var rol = ResolverRol(ctx, rolStr);
            if (rol is null)
                return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Roles:NoEncontrado"), desc);

            if (ctx.Guild.CurrentMember.Hierarchy <= rol.Position)
                return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Roles:JerarquiaBot", ("rol", rol.Name)), desc);

            if (ctx.Guild.OwnerId != ctx.Miembro.Id && ctx.Miembro.Hierarchy <= rol.Position)
                return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Roles:JerarquiaUsuario", ("rol", rol.Name)), desc);

            if (miembro.Roles.Any(r => r.Id == rol.Id))
                return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Roles:YaTiene", ("usuario", miembro.DisplayName), ("rol", rol.Name)), desc);

            await miembro.GrantRoleAsync(rol, $"Asignado por IA a petición de {ctx.Miembro.Username} ({ctx.Miembro.Id})").ConfigureAwait(false);
            return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Roles:Asignado", ("usuario", miembro.DisplayName), ("rol", rol.Name)), desc);
        });

    private ToolDef ToolRoleRemove() => new(
        "role_remove",
        "Remove a role from a server member. Requires ManageRoles permission on both the user and the bot.",
        Esquema(("user", "string", "The user to remove the role from: mention (<@id>), ID or username."),
                ("role", "string", "The role name, mention (<@&id>) or ID to remove.")),
        Destructivo: false,
        DescripcionComando: null,
        Ejecutar: async (ctx, args) =>
        {
            var desc = "/role remove";
            if (await ChequearPermisoGuild(ctx, Permissions.ManageRoles, desc) is { } error) return error;

            var usuarioStr = ArgString(args, "user");
            var rolStr = ArgString(args, "role");

            var miembro = await ResolverUsuarioAsync(ctx, usuarioStr).ConfigureAwait(false);
            if (miembro is null)
                return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Moderacion:NoMiembro"), desc);

            var rol = ResolverRol(ctx, rolStr);
            if (rol is null)
                return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Roles:NoEncontrado"), desc);

            if (ctx.Guild.CurrentMember.Hierarchy <= rol.Position)
                return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Roles:JerarquiaBot", ("rol", rol.Name)), desc);

            if (ctx.Guild.OwnerId != ctx.Miembro.Id && ctx.Miembro.Hierarchy <= rol.Position)
                return new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Roles:JerarquiaUsuario", ("rol", rol.Name)), desc);

            if (!miembro.Roles.Any(r => r.Id == rol.Id))
                return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Roles:NoTiene", ("usuario", miembro.DisplayName), ("rol", rol.Name)), desc);

            await miembro.RevokeRoleAsync(rol, $"Quitado por IA a petición de {ctx.Miembro.Username} ({ctx.Miembro.Id})").ConfigureAwait(false);
            return new AiCommandResult(true, _msg.Get(ctx.Guild.Id, "Roles:Removido", ("usuario", miembro.DisplayName), ("rol", rol.Name)), desc);
        });

    private ToolDef ToolCalculate() => new(
        "math_calculate",
        "Evaluate a mathematical or scientific expression with high precision (supports PEMDAS, (), [], {}, fractions, roots, logs, trig, factorials).",
        Esquema(("expression", "string", "The math expression to evaluate, e.g. '5^2 + sqrt(144)', '3(9/3/2)', 'log(1000)'.")),
        Destructivo: false,
        DescripcionComando: null,
        Ejecutar: (ctx, args) =>
        {
            var desc = "/calc";
            var expr = ArgString(args, "expression");
            if (string.IsNullOrWhiteSpace(expr))
                return Task.FromResult(new AiCommandResult(false, _msg.Get(ctx.Guild.Id, "Calculadora:ErrorSintaxis", ("error", "Expresión vacía")), desc));

            var res = MathEngine.Evaluar(expr);
            if (res.Exitoso)
            {
                var texto = string.IsNullOrEmpty(res.FraccionExacta)
                    ? $"{expr} = {res.Resultado.ToString(CultureInfo.InvariantCulture)}"
                    : $"{expr} = {res.Resultado.ToString(CultureInfo.InvariantCulture)} ({res.FraccionExacta})";
                return Task.FromResult(new AiCommandResult(true, texto, desc));
            }

            var err = _msg.Get(ctx.Guild.Id, res.ErrorClave ?? "Calculadora:ErrorDesconocido", ("error", res.ErrorDetalle ?? ""));
            return Task.FromResult(new AiCommandResult(false, err, desc));
        });

    // ------------------------- catálogo -------------------------

    private Dictionary<string, ToolDef> ConstruirCatalogo()
    {
        var catalog = new Dictionary<string, ToolDef>();
        foreach (var t in CatalogoBase().Concat(CatalogoMusica()).Concat(CatalogoConfiguracion()).Concat(CatalogoEstado()))
            catalog[t.Nombre] = t;
        return catalog;
    }

    private IEnumerable<ToolDef> CatalogoBase()
    {
        yield return ToolBan();
        yield return ToolKick();
        yield return ToolTimeout();
        yield return ToolUntimeout();
        yield return ToolWarn();
        yield return ToolHistory();
        yield return ToolLock();
        yield return ToolUnlock();
        yield return ToolClear();
        yield return ToolRoleAdd();
        yield return ToolRoleRemove();
        yield return ToolCalculate();
    }
}
