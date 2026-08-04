using Microsoft.EntityFrameworkCore;
using Snowflake.Bot.Data.Entities;

namespace Snowflake.Bot.Data;

/// <summary>
/// Contexto de base de datos SQLite del bot.
/// </summary>
public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
{
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<GuildConfig> GuildConfigs => Set<GuildConfig>();
    public DbSet<ColorRole> ColorRoles => Set<ColorRole>();
    public DbSet<TempChannel> TempChannels => Set<TempChannel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Incident>(e =>
        {
            e.HasKey(i => i.Id);
            // Guardar el enum como texto para que la base de datos sea legible.
            e.Property(i => i.Type).HasConversion<string>();
            e.HasIndex(i => new { i.GuildId, i.TargetUserId });
        });

        modelBuilder.Entity<GuildConfig>(e =>
        {
            e.HasKey(g => g.GuildId);
        });

        modelBuilder.Entity<ColorRole>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => new { c.GuildId, c.RoleId }).IsUnique();
            e.HasIndex(c => c.GuildId);
        });

        modelBuilder.Entity<TempChannel>(e =>
        {
            e.HasKey(t => t.ChannelId);
            e.HasIndex(t => t.GuildId);
        });
    }
}
