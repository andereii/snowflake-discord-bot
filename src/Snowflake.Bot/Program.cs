using DotNetEnv;
using DSharpPlus;
using Lavalink4NET.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Snowflake.Bot.Configuration;
using Snowflake.Bot.Data;
using Snowflake.Bot.Services;

// Carga las variables del archivo .env (si existe); nunca sobreescribe las del sistema.
Env.TraversePath().Load();

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    // appsettings.json se copia junto al ejecutable al compilar: así la config
    // se encuentra siempre, sin importar desde qué directorio se ejecute el bot.
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.Configure<BotConfiguration>(builder.Configuration.GetSection("Bot"));

// Todos los textos del bot, editables sin tocar el código (recarga en caliente).
builder.Configuration.AddJsonFile("messages.json", optional: false, reloadOnChange: true);
builder.Services.AddSingleton<MessagesService>();

// Base de datos SQLite junto al ejecutable.
builder.Services.AddDbContextFactory<BotDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(AppContext.BaseDirectory, "snowflake.db")}"));

// Cliente HTTP usado por el fallback de canciones de Spotify.
builder.Services.AddHttpClient("Spotify", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SnowflakeBot/1.0");
});

// Lavalink (música).
var lavalink = builder.Configuration.GetSection("Lavalink").Get<LavalinkOptions>()
    ?? new LavalinkOptions();
builder.Services.AddLavalink();
builder.Services.ConfigureLavalink(o =>
{
    o.BaseAddress = new Uri($"http://{lavalink.Host}:{lavalink.Port}");
    o.Passphrase = lavalink.Password;
    o.Label = "snowflake";
});

builder.Services.AddSingleton<ModerationLogService>();
builder.Services.AddSingleton<DownloadService>();
builder.Services.AddSingleton<LitterboxService>();
builder.Services.AddSingleton<ColorService>();
builder.Services.AddSingleton<VoiceHubService>();
builder.Services.AddSingleton<MusicService>();
builder.Services.AddSingleton<MusicWidgetService>();

builder.Services.AddSingleton(sp =>
{
    var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");
    if (string.IsNullOrWhiteSpace(token))
    {
        throw new InvalidOperationException(
            "No se encontró la variable DISCORD_TOKEN. Copia .env.example a .env y pega tu token.");
    }

    return new DiscordClient(new DiscordConfiguration
    {
        Token = token,
        TokenType = TokenType.Bot,
        Intents = DiscordIntents.Guilds
                | DiscordIntents.GuildMembers
                | DiscordIntents.GuildBans
                | DiscordIntents.GuildVoiceStates
                | DiscordIntents.GuildMessages,
        LoggerFactory = sp.GetRequiredService<ILoggerFactory>()
    });
});

builder.Services.AddHostedService<DiscordBotService>();

var app = builder.Build();

// Crea/actualiza la base de datos aplicando las migraciones de EF Core.
{
    await using var db = await app.Services
        .GetRequiredService<IDbContextFactory<BotDbContext>>()
        .CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

await app.RunAsync();
