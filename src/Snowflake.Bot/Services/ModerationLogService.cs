using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Snowflake.Bot.Data;
using Snowflake.Bot.Data.Entities;
using Snowflake.Bot.Services.Settings;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Services;

/// <summary>
/// Registra incidentes de moderación en la base de datos y los anuncia
/// en el canal de logs configurado del servidor.
/// </summary>
public sealed class ModerationLogService(
    IDbContextFactory<BotDbContext> dbFactory,
    GuildSettingsService settings,
    MessagesService msg,
    ILogger<ModerationLogService> logger)
{
    /// <summary>Guarda el incidente en la base de datos y le asigna número de caso.</summary>
    public async Task<Incident> RegistrarAsync(
        ulong guildId,
        DiscordUser objetivo,
        DiscordUser moderador,
        IncidentType tipo,
        string motivo,
        TimeSpan? duracion = null)
        => await RegistrarAsync(guildId, objetivo.Id, objetivo.Username, moderador, tipo, motivo, duracion)
            .ConfigureAwait(false);

    /// <summary>
    /// Igual que <see cref="RegistrarAsync(ulong, DiscordUser, DiscordUser, IncidentType, string, TimeSpan?)"/>
    /// pero con el objetivo identificado por ID/nombre (para vetos a usuarios
    /// que ya no están en el servidor).
    /// </summary>
    public async Task<Incident> RegistrarAsync(
        ulong guildId,
        ulong targetId,
        string targetTag,
        DiscordUser moderador,
        IncidentType tipo,
        string motivo,
        TimeSpan? duracion = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var incidente = new Incident
        {
            GuildId = guildId,
            TargetUserId = targetId,
            TargetTag = targetTag,
            ModeratorId = moderador.Id,
            ModeratorTag = moderador.Username,
            Type = tipo,
            Reason = motivo,
            Duration = duracion,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Incidents.Add(incidente);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Incidente #{Id} ({Tipo}): {Objetivo} sancionado por {Moderador} en {Guild}",
            incidente.Id, tipo, targetId, moderador.Id, guildId);

        return incidente;
    }

    /// <summary>Publica el embed del incidente en el canal de logs, si hay uno configurado.</summary>
    public async Task AnunciarAsync(DiscordGuild guild, Incident incidente)
    {
        var config = await settings.GetAsync(guild.Id);
        if (config.ModLogChannelId is not ulong canalId) return;

        var canal = guild.GetChannel(canalId);
        if (canal is null)
        {
            logger.LogWarning("El canal de logs {CanalId} del servidor {Guild} ya no existe", canalId, guild.Id);
            return;
        }

        await canal.SendMessageAsync(CrearEmbedIncidente(incidente));
    }

    /// <summary>
    /// Intenta avisar al usuario por MD antes de una acción (si tiene los MD
    /// abiertos). Compartido por los comandos slash y el ejecutor de comandos
    /// por IA para que ambos actúen igual.
    /// </summary>
    public async Task AvisarPrivadoAsync(DiscordMember miembro, string servidor, string accion, string motivo)
    {
        try
        {
            var dm = await miembro.CreateDmChannelAsync();
            var embed = new DiscordEmbedBuilder()
                .WithTitle(msg.Get(miembro.Guild.Id, "Moderacion:Dm:Titulo",
                    ("accion", accion), ("servidor", servidor)))
                .WithColor(DiscordColor.Red)
                .AddField(msg.Get(miembro.Guild.Id, "Moderacion:Dm:CampoMotivo"), motivo);
            await dm.SendMessageAsync(embed);
        }
        catch
        {
            // Tiene los mensajes directos cerrados: se continúa sin avisar.
        }
    }

    /// <summary>Construye el embed estándar de un incidente.</summary>
    public DiscordEmbed CrearEmbedIncidente(Incident i)
    {
        var color = i.Type switch
        {
            IncidentType.Advertencia => DiscordColor.Yellow,
            IncidentType.Expulsion => DiscordColor.Orange,
            IncidentType.Veto => DiscordColor.Red,
            IncidentType.Aislamiento => DiscordColor.Purple,
            IncidentType.FinAislamiento => DiscordColor.Green,
            _ => DiscordColor.Gray
        };

        var embed = new DiscordEmbedBuilder()
            .WithTitle($"{msg.Get(i.GuildId, $"Moderacion:Tipos:{i.Type}")} · {msg.Get(i.GuildId, "Moderacion:Caso", ("caso", i.Id))}")
            .WithColor(color)
            .AddField(msg.Get(i.GuildId, "Moderacion:Campos:Usuario"), $"<@{i.TargetUserId}> ({i.TargetTag})", true)
            .AddField(msg.Get(i.GuildId, "Moderacion:Campos:Moderador"), $"<@{i.ModeratorId}> ({i.ModeratorTag})", true);

        if (i.Duration is { } d)
            embed.AddField(msg.Get(i.GuildId, "Moderacion:Campos:Duracion"), DurationParser.Format(d, msg.Locale(i.GuildId)), true);

        return embed
            .AddField(msg.Get(i.GuildId, "Moderacion:Campos:Motivo"), i.Reason)
            .WithTimestamp(i.CreatedAt)
            .Build();
    }
}
