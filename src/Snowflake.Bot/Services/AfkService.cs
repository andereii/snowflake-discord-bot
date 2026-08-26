using System.Collections.Concurrent;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Snowflake.Bot.Data;
using Snowflake.Bot.Data.Entities;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Services;

/// <summary>
/// Servicio central de gestión del estado de ausencia (AFK),
/// detección de retorno, aviso de menciones y canales ignorados.
/// </summary>
public sealed class AfkService
{
    private readonly IDbContextFactory<BotDbContext> _dbFactory;
    private readonly MessagesService _msg;
    private readonly ILogger<AfkService> _logger;

    // Caché en memoria: (GuildId, UserId) => AfkUser
    private readonly ConcurrentDictionary<(ulong GuildId, ulong UserId), AfkUser> _afkUsers = new();

    // Caché de canales ignorados: GuildId => HashSet de ChannelId
    private readonly ConcurrentDictionary<ulong, HashSet<ulong>> _ignoredChannels = new();

    // Control de spam para menciones: (GuildId, ChannelId, UserId) => ExpiraEn
    private readonly ConcurrentDictionary<(ulong GuildId, ulong ChannelId, ulong UserId), DateTimeOffset> _mentionCooldowns = new();

    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public AfkService(
        IDbContextFactory<BotDbContext> dbFactory,
        MessagesService msg,
        ILogger<AfkService> logger)
    {
        _dbFactory = dbFactory;
        _msg = msg;
        _logger = logger;
    }

    /// <summary>
    /// Carga el estado inicial de la base de datos a la memoria.
    /// </summary>
    public async Task InicializarAsync()
    {
        if (_initialized) return;
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            await using var db = await _dbFactory.CreateDbContextAsync();
            var afks = await db.AfkUsers.AsNoTracking().ToListAsync();
            foreach (var afk in afks)
            {
                _afkUsers[(afk.GuildId, afk.UserId)] = afk;
            }

            var ignorados = await db.AfkIgnoredChannels.AsNoTracking().ToListAsync();
            foreach (var ig in ignorados)
            {
                var set = _ignoredChannels.GetOrAdd(ig.GuildId, _ => new HashSet<ulong>());
                lock (set)
                {
                    set.Add(ig.ChannelId);
                }
            }

            _initialized = true;
            _logger.LogInformation("AfkService inicializado con {Afks} usuarios y {Ignorados} canales ignorados.", afks.Count, ignorados.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al inicializar AfkService.");
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Establece el estado de ausencia (AFK) para un miembro.
    /// </summary>
    public async Task EstablecerAfkAsync(DiscordGuild guild, DiscordMember member, string? motivo)
    {
        await InicializarAsync();

        var motivoLimpio = string.IsNullOrWhiteSpace(motivo) ? "AFK" : motivo.Trim();
        if (motivoLimpio.Length > 250)
            motivoLimpio = motivoLimpio[..250];

        string? originalNick = null;
        if (member.Nickname is not null)
            originalNick = member.Nickname;

        var afk = new AfkUser
        {
            GuildId = guild.Id,
            UserId = member.Id,
            Reason = motivoLimpio,
            SetAt = DateTimeOffset.UtcNow,
            OriginalNickname = originalNick
        };

        _afkUsers[(guild.Id, member.Id)] = afk;

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            var existente = await db.AfkUsers.FirstOrDefaultAsync(a => a.GuildId == guild.Id && a.UserId == member.Id);
            if (existente is not null)
            {
                existente.Reason = motivoLimpio;
                existente.SetAt = afk.SetAt;
                existente.OriginalNickname = originalNick;
            }
            else
            {
                db.AfkUsers.Add(afk);
            }
            await db.SaveChangesAsync();
        }

        // Intento de modificar el apodo para reflejar [AFK]
        await IntentarModificarApodoAfkAsync(guild, member);
    }

    /// <summary>
    /// Remueve el estado AFK de un miembro (manualmente o por retorno).
    /// </summary>
    public async Task<bool> RemoverAfkAsync(DiscordGuild guild, DiscordMember member)
    {
        await InicializarAsync();

        if (!_afkUsers.TryRemove((guild.Id, member.Id), out var afk))
            return false;

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            var entity = await db.AfkUsers.FirstOrDefaultAsync(a => a.GuildId == guild.Id && a.UserId == member.Id);
            if (entity is not null)
            {
                db.AfkUsers.Remove(entity);
                await db.SaveChangesAsync();
            }
        }

        // Intento de restaurar apodo original
        await IntentarRestaurarApodoAsync(guild, member, afk.OriginalNickname);
        return true;
    }

    /// <summary>
    /// Remueve todos los usuarios AFK de un servidor (comando de moderación).
    /// </summary>
    public async Task<int> RemoverTodosAfkAsync(DiscordGuild guild)
    {
        await InicializarAsync();

        var clavesServidor = _afkUsers.Keys.Where(k => k.GuildId == guild.Id).ToList();
        foreach (var clave in clavesServidor)
        {
            if (_afkUsers.TryRemove(clave, out var afk))
            {
                try
                {
                    var member = await guild.GetMemberAsync(clave.UserId);
                    await IntentarRestaurarApodoAsync(guild, member, afk.OriginalNickname);
                }
                catch
                {
                    // Miembro pudo haber salido del servidor
                }
            }
        }

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            var entities = await db.AfkUsers.Where(a => a.GuildId == guild.Id).ToListAsync();
            db.AfkUsers.RemoveRange(entities);
            await db.SaveChangesAsync();
            return entities.Count;
        }
    }

    /// <summary>
    /// Restablece el motivo de ausencia de un usuario al valor por defecto ("AFK").
    /// </summary>
    public async Task<bool> ResetearMotivoAfkAsync(ulong guildId, ulong userId)
    {
        await InicializarAsync();

        if (_afkUsers.TryGetValue((guildId, userId), out var afk))
        {
            afk.Reason = "AFK";

            await using var db = await _dbFactory.CreateDbContextAsync();
            var entity = await db.AfkUsers.FirstOrDefaultAsync(a => a.GuildId == guildId && a.UserId == userId);
            if (entity is not null)
            {
                entity.Reason = "AFK";
                await db.SaveChangesAsync();
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Obtiene los datos AFK de un usuario si está ausente.
    /// </summary>
    public AfkUser? ObtenerAfk(ulong guildId, ulong userId)
    {
        _afkUsers.TryGetValue((guildId, userId), out var afk);
        return afk;
    }

    /// <summary>
    /// Lista todos los usuarios ausentes de un servidor.
    /// </summary>
    public IReadOnlyList<AfkUser> ListarAfk(ulong guildId)
    {
        return _afkUsers.Values.Where(a => a.GuildId == guildId).OrderBy(a => a.SetAt).ToList();
    }

    /// <summary>
    /// Agrega un canal a la lista de canales ignorados.
    /// </summary>
    public async Task<bool> AgregarCanalIgnoradoAsync(ulong guildId, ulong channelId)
    {
        await InicializarAsync();

        var set = _ignoredChannels.GetOrAdd(guildId, _ => new HashSet<ulong>());
        lock (set)
        {
            if (!set.Add(channelId))
                return false;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var existente = await db.AfkIgnoredChannels.FirstOrDefaultAsync(c => c.GuildId == guildId && c.ChannelId == channelId);
        if (existente is null)
        {
            db.AfkIgnoredChannels.Add(new AfkIgnoredChannel { GuildId = guildId, ChannelId = channelId });
            await db.SaveChangesAsync();
        }

        return true;
    }

    /// <summary>
    /// Remueve un canal de la lista de canales ignorados.
    /// </summary>
    public async Task<bool> RemoverCanalIgnoradoAsync(ulong guildId, ulong channelId)
    {
        await InicializarAsync();

        if (_ignoredChannels.TryGetValue(guildId, out var set))
        {
            lock (set)
            {
                set.Remove(channelId);
            }
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.AfkIgnoredChannels.FirstOrDefaultAsync(c => c.GuildId == guildId && c.ChannelId == channelId);
        if (entity is not null)
        {
            db.AfkIgnoredChannels.Remove(entity);
            await db.SaveChangesAsync();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Obtiene todos los IDs de canales ignorados en el servidor.
    /// </summary>
    public IReadOnlyList<ulong> ObtenerCanalesIgnorados(ulong guildId)
    {
        if (_ignoredChannels.TryGetValue(guildId, out var set))
        {
            lock (set)
            {
                return set.ToList();
            }
        }
        return Array.Empty<ulong>();
    }

    /// <summary>
    /// Verifica si un canal está marcado como ignorado.
    /// </summary>
    public bool EsCanalIgnorado(ulong guildId, ulong channelId)
    {
        if (_ignoredChannels.TryGetValue(guildId, out var set))
        {
            lock (set)
            {
                return set.Contains(channelId);
            }
        }
        return false;
    }

    /// <summary>
    /// Procesa cada mensaje creado en el servidor para detectar retornos de usuarios AFK
    /// o menciones a usuarios que están ausentes.
    /// </summary>
    public async Task ProcesarMensajeAsync(DiscordClient client, MessageCreateEventArgs e)
    {
        if (e.Guild is null || e.Author.IsBot)
            return;

        await InicializarAsync();

        // 1. Detección de retorno de usuario AFK
        if (_afkUsers.TryGetValue((e.Guild.Id, e.Author.Id), out var afk))
        {
            if (!EsCanalIgnorado(e.Guild.Id, e.Channel.Id))
            {
                var duracion = DateTimeOffset.UtcNow - afk.SetAt;
                // Si el estado AFK se acaba de poner hace menos de 3 segundos, no lo quitamos al instante
                if (duracion.TotalSeconds >= 3)
                {
                    var member = e.Author as DiscordMember ?? await e.Guild.GetMemberAsync(e.Author.Id);
                    await RemoverAfkAsync(e.Guild, member);

                    var timestampRelativo = $"<t:{afk.SetAt.ToUnixTimeSeconds()}:R>";
                    var textoRetorno = _msg.Get(e.Guild.Id, "Afk:BienvenidaRetorno",
                        ("usuario", e.Author.Mention),
                        ("tiempo", timestampRelativo));

                    var msgRetorno = await e.Channel.SendMessageAsync(textoRetorno);

                    // Auto-eliminar el mensaje de bienvenida después de 10 segundos
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10));
                            await msgRetorno.DeleteAsync();
                        }
                        catch
                        {
                            // Ignorar si ya fue borrado o faltan permisos
                        }
                    });
                }
            }
        }

        // 2. Detección de menciones a usuarios AFK
        if (e.Message.MentionedUsers is { Count: > 0 } mencionados)
        {
            var ahora = DateTimeOffset.UtcNow;
            foreach (var mencionado in mencionados)
            {
                if (mencionado.Id == e.Author.Id || mencionado.IsBot)
                    continue;

                if (_afkUsers.TryGetValue((e.Guild.Id, mencionado.Id), out var afkTarget))
                {
                    var claveCooldown = (e.Guild.Id, e.Channel.Id, mencionado.Id);
                    if (_mentionCooldowns.TryGetValue(claveCooldown, out var expira) && expira > ahora)
                        continue;

                    _mentionCooldowns[claveCooldown] = ahora.AddSeconds(8);

                    var timestampRelativo = $"<t:{afkTarget.SetAt.ToUnixTimeSeconds()}:R>";
                    var textoMencion = _msg.Get(e.Guild.Id, "Afk:MencionAusente",
                        ("usuario", mencionado.Username),
                        ("motivo", afkTarget.Reason),
                        ("tiempo", timestampRelativo));

                    await e.Channel.SendMessageAsync(textoMencion);
                }
            }
        }
    }

    private static async Task IntentarModificarApodoAfkAsync(DiscordGuild guild, DiscordMember member)
    {
        try
        {
            var botMember = guild.CurrentMember;
            if (!botMember.Permissions.HasPermission(Permissions.ManageNicknames))
                return;

            if (guild.OwnerId == member.Id || member.Hierarchy >= botMember.Hierarchy)
                return;

            var baseNick = member.Nickname ?? member.Username;
            if (baseNick.StartsWith("[AFK] "))
                return;

            var nuevoNick = $"[AFK] {baseNick}";
            if (nuevoNick.Length > 32)
                nuevoNick = nuevoNick[..32];

            await member.ModifyAsync(m => m.Nickname = nuevoNick);
        }
        catch
        {
            // Ignorar fallos de permisos o jerarquía
        }
    }

    private static async Task IntentarRestaurarApodoAsync(DiscordGuild guild, DiscordMember member, string? originalNick)
    {
        try
        {
            var botMember = guild.CurrentMember;
            if (!botMember.Permissions.HasPermission(Permissions.ManageNicknames))
                return;

            if (guild.OwnerId == member.Id || member.Hierarchy >= botMember.Hierarchy)
                return;

            var currentNick = member.Nickname ?? member.Username;
            if (!currentNick.StartsWith("[AFK] "))
                return;

            await member.ModifyAsync(m => m.Nickname = originalNick ?? "");
        }
        catch
        {
            // Ignorar fallos de permisos o jerarquía
        }
    }
}
