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
    public DbSet<CountingConfig> CountingConfigs => Set<CountingConfig>();
    public DbSet<CountingStat> CountingStats => Set<CountingStat>();
    public DbSet<TriviaStat> TriviaStats => Set<TriviaStat>();
    public DbSet<AfkUser> AfkUsers => Set<AfkUser>();
    public DbSet<AfkIgnoredChannel> AfkIgnoredChannels => Set<AfkIgnoredChannel>();
    public DbSet<YouTubeSubscription> YouTubeSubscriptions => Set<YouTubeSubscription>();
    public DbSet<ChannelLock> ChannelLocks => Set<ChannelLock>();
    public DbSet<HardmuteBackup> HardmuteBackups => Set<HardmuteBackup>();
    public DbSet<Birthday> Birthdays => Set<Birthday>();
    public DbSet<BirthdayConfig> BirthdayConfigs => Set<BirthdayConfig>();

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
            // Las nuevas capacidades se activan por defecto también en los
            // servidores que ya existían antes de la migración.
            e.Property(g => g.AiChatEnabled).HasDefaultValue(true);
            e.Property(g => g.DownloadsEnabled).HasDefaultValue(true);
            e.Property(g => g.AiWebSearchEnabled).HasDefaultValue(true);
            e.Property(g => g.AiCommandsEnabled).HasDefaultValue(true);
            e.Property(g => g.Language).HasDefaultValue("en");
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

        modelBuilder.Entity<CountingConfig>(e =>
        {
            e.HasKey(c => c.GuildId);
            // Base enum guardada como texto para legibilidad.
            e.Property(c => c.Base).HasConversion<string>();
        });

        modelBuilder.Entity<CountingStat>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.GuildId, s.UserId }).IsUnique();
            e.HasIndex(s => s.GuildId);
        });

        modelBuilder.Entity<TriviaStat>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.GuildId, s.UserId }).IsUnique();
            e.HasIndex(s => s.GuildId);
        });

        modelBuilder.Entity<AfkUser>(e =>
        {
            e.HasKey(a => new { a.GuildId, a.UserId });
            e.HasIndex(a => a.GuildId);
        });

        modelBuilder.Entity<AfkIgnoredChannel>(e =>
        {
            e.HasKey(c => new { c.GuildId, c.ChannelId });
            e.HasIndex(c => c.GuildId);
        });

        modelBuilder.Entity<YouTubeSubscription>(e =>
        {
            e.HasKey(y => y.GuildId);
            // Varios servidores pueden seguir el mismo canal de YT: índice para
            // agruparlos y hacer un único fetch del feed por canal.
            e.HasIndex(y => y.YTChannelId);
        });

        modelBuilder.Entity<ChannelLock>(e =>
        {
            e.HasKey(l => l.ChannelId);
            e.HasIndex(l => l.GuildId);
        });

        modelBuilder.Entity<HardmuteBackup>(e =>
        {
            e.HasKey(h => h.Id);
            e.HasIndex(h => new { h.GuildId, h.UserId }).IsUnique();
        });

        modelBuilder.Entity<Birthday>(e =>
        {
            e.HasKey(b => new { b.GuildId, b.UserId });
            e.HasIndex(b => new { b.GuildId, b.Month, b.Day });
        });

        modelBuilder.Entity<BirthdayConfig>(e =>
        {
            e.HasKey(c => c.GuildId);
        });
    }
}
