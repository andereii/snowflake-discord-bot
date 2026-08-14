using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snowflake.Bot.Configuration;
using Snowflake.Bot.Data;
using Snowflake.Bot.Data.Entities;

namespace Snowflake.Bot.Services;

/// <summary>
/// Gestiona la paleta de colores del servidor: roles de color que los
/// usuarios se autoasignan desde un menú de selección.
/// </summary>
public sealed class ColorService(
    IDbContextFactory<BotDbContext> dbFactory,
    MessagesService msg,
    IOptionsMonitor<ColorOptions> options,
    ILogger<ColorService> logger)
{
    /// <summary>Custom id del menú de selección de color (component interaction).</summary>
    public const string CustomId = "snowflake_colores";

    public enum PaletaType { Normal, Pastel }

    private (string Nombre, int Hex)[] Paleta(PaletaType t)
    {
        var opts = options.CurrentValue;
        var dict = t == PaletaType.Pastel ? opts.Pastel : opts.Normal;
        return dict.Select(kvp => (kvp.Key, Convert.ToInt32(kvp.Value, 16))).ToArray();
    }

    /// <summary>
    /// Instala la paleta elegida. Si hubiera otra paleta instalada, la reemplaza.
    /// Devuelve (creados, removidos, total). Si creados==0 y removidos==0 ya estaba.
    /// </summary>
    public async Task<(int Creados, int Removidos, int Total)> InstalarAsync(
        DiscordGuild guild, PaletaType paleta)
    {
        var colores = Paleta(paleta);
        var nombresPaleta = colores.Select(c => c.Nombre).ToHashSet();

        await using var db = await dbFactory.CreateDbContextAsync();
        var existentes = await db.ColorRoles
            .Where(c => c.GuildId == guild.Id)
            .ToListAsync();

        // 1) Quitar los color roles que pertenezcan a OTRA paleta.
        var removidos = 0;
        var aQuitar = existentes.Where(c => !nombresPaleta.Contains(c.Name)).ToList();
        foreach (var c in aQuitar)
        {
            var role = guild.GetRole(c.RoleId);
            if (role is not null)
            {
                try { await role.DeleteAsync("Cambio de paleta de colores"); removidos++; }
                catch (Exception ex) { logger.LogWarning(ex, "No se pudo borrar el rol {Id}", c.RoleId); }
            }
            db.ColorRoles.Remove(c);
        }

        // 2) Crear los color roles de esta paleta que falten.
        var creados = 0;
        var existentesNombres = existentes.Select(c => c.Name).ToHashSet();
        foreach (var (nombre, hex) in colores)
        {
            if (existentesNombres.Contains(nombre)) continue;

            DiscordRole role;
            try
            {
                role = await guild.CreateRoleAsync(
                    $"• {nombre}",
                    color: new DiscordColor(hex),
                    hoist: false,
                    mentionable: false,
                    reason: "Paleta de colores instalada");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo crear el rol de color {Nombre}", nombre);
                continue;
            }

            db.ColorRoles.Add(new ColorRole
            {
                GuildId = guild.Id,
                RoleId = role.Id,
                Name = nombre,
                ColorHex = hex.ToString("X6")
            });
            creados++;
        }

        await db.SaveChangesAsync();
        return (creados, removidos, colores.Length);
    }

    /// <summary>Elimina todos los roles de la paleta y sus registros.</summary>
    public async Task<int> DesinstalarAsync(DiscordGuild guild)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var roles = await db.ColorRoles.Where(c => c.GuildId == guild.Id).ToListAsync();

        var borrados = 0;
        foreach (var c in roles)
        {
            var role = guild.GetRole(c.RoleId);
            if (role is not null)
            {
                try { await role.DeleteAsync("Desinstalando paleta de colores"); borrados++; }
                catch (Exception ex) { logger.LogWarning(ex, "No se pudo borrar el rol {Id}", c.RoleId); }
            }
        }

        db.ColorRoles.RemoveRange(roles);
        await db.SaveChangesAsync();
        return borrados;
    }

    /// <summary>Quita al miembro todos los color roles instalados. Devuelve si tenía alguno.</summary>
    public async Task<bool> QuitarAsync(DiscordMember miembro, ulong guildId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var instalados = await db.ColorRoles
            .Where(c => c.GuildId == guildId)
            .Select(c => c.RoleId)
            .ToListAsync();

        var rolesInstalados = instalados.ToHashSet();
        var tenia = false;

        foreach (var r in miembro.Roles.Where(rr => rolesInstalados.Contains(rr.Id)))
        {
            await miembro.RevokeRoleAsync(r, "El usuario se quitó el color");
            tenia = true;
        }

        return tenia;
    }

    /// <summary>Construye el embed + menú de selección para que un usuario elija color.</summary>
    public async Task<(DiscordEmbedBuilder Embed, DiscordSelectComponent Select)?> ConstruirSelectorAsync(DiscordGuild guild)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var roles = await db.ColorRoles
            .Where(c => c.GuildId == guild.Id)
            .OrderBy(c => c.Id)
            .ToListAsync();

        if (roles.Count == 0) return null;

        var opciones = roles
            .Select(c => new DiscordSelectComponentOption($"• {c.Name}", c.RoleId.ToString(), $"#{c.ColorHex}"))
            .Take(24)
            .ToList();
        opciones.Add(new DiscordSelectComponentOption("Quitar color", "0", "Te quita el color actual"));

        var select = new DiscordSelectComponent(CustomId, "Elige tu color…", opciones);

        var embed = new DiscordEmbedBuilder()
            .WithTitle(msg.Get(guild.Id, "Colores:Titulo"))
            .WithDescription(msg.Get(guild.Id, "Colores:Descripcion"))
            .WithColor(DiscordColor.Azure);

        return (embed, select);
    }

    /// <summary>Lista los colores instalados (para /colores listar).</summary>
    public async Task<List<ColorRole>> ListarAsync(ulong guildId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ColorRoles.Where(c => c.GuildId == guildId).OrderBy(c => c.Id).ToListAsync();
    }

    /// <summary>Maneja la selección en el menú de color.</summary>
    public async Task HandleSelectAsync(ComponentInteractionCreateEventArgs e)
    {
        if (e.Guild is null || e.Values is null || e.Values.Length == 0) return;
        var valor = e.Values[0];

        try
        {
            if (!ulong.TryParse(valor, out var roleId)) roleId = 0;

            await using var db = await dbFactory.CreateDbContextAsync();
            var instalados = await db.ColorRoles.Where(c => c.GuildId == e.Guild.Id).ToListAsync();

            var miembro = await e.Guild.GetMemberAsync(e.User.Id);
            var nombreColor = await AplicarAsync(miembro, instalados, e.Guild, roleId);

            var texto = roleId == 0
                ? msg.Get(e.Guild.Id, "Colores:Quitado")
                : msg.Get(e.Guild.Id, "Colores:Aplicado", ("color", nombreColor));

            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder().WithContent(texto));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error aplicando color al usuario {User}", e.User.Id);
            try
            {
                await e.Interaction.CreateResponseAsync(
                    InteractionResponseType.UpdateMessage,
                    new DiscordInteractionResponseBuilder().WithContent(msg.Get(e.Guild.Id, "Colores:Error")));
            }
            catch { /* no se pudo notificar */ }
        }
    }

    /// <summary>Quita los color roles actuales del miembro y, si roleId != 0, le asigna el nuevo.</summary>
    private static async Task<string> AplicarAsync(
        DiscordMember miembro, List<ColorRole> instalados, DiscordGuild guild, ulong roleId)
    {
        foreach (var c in instalados)
        {
            var r = miembro.Roles.FirstOrDefault(rr => rr.Id == c.RoleId);
            if (r is not null) await miembro.RevokeRoleAsync(r, "Cambio de color");
        }

        if (roleId == 0) return "sin color";

        var nuevo = guild.GetRole(roleId);
        if (nuevo is null) return "sin color";

        await miembro.GrantRoleAsync(nuevo, "Color elegido");
        return nuevo.Name;
    }
}