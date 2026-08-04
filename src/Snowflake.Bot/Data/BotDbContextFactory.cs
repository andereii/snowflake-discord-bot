using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Snowflake.Bot.Data;

/// <summary>
/// Fábrica de diseño para que `dotnet ef migrations` pueda crear el
/// BotDbContext en tiempo de diseño (sin tener que construir el host completo).
/// </summary>
public sealed class BotDbContextFactory : IDesignTimeDbContextFactory<BotDbContext>
{
    public BotDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite("Data Source=snowflake.db")
            .Options;
        return new BotDbContext(options);
    }
}