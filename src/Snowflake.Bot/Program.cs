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
using Snowflake.Bot.Endpoints;
using Snowflake.Bot.Services;
using Snowflake.Bot.Services.AiCommands;
using Snowflake.Bot.Services.Calculators;
using Snowflake.Bot.Services.PrefixCommands;
using Snowflake.Bot.Services.Settings;

// Carga las variables del archivo .env (si existe); nunca sobreescribe las del sistema.
Env.TraversePath().Load();

// WebApplication: el proceso aloja a la vez el bot de Discord y la API REST
// que consumirá el panel web de configuración (ver Endpoints/ConfigEndpoints).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // appsettings.json se copia junto al ejecutable al compilar: así la config
    // se encuentra siempre, sin importar desde qué directorio se ejecute el bot.
    ContentRootPath = AppContext.BaseDirectory
});

// Configuración global (appsettings.json + .env), recargable en caliente.
builder.Services.AddSnowflakeOptions(builder.Configuration);

// Textos localizados del bot (recarga en caliente). REGLA: todo mensaje nuevo
// debe existir en los tres archivos (en/es/pt); el inglés es el idioma base.
builder.Configuration.AddJsonFile("messages.en.json", optional: false, reloadOnChange: true);
builder.Configuration.AddJsonFile("messages.es.json", optional: false, reloadOnChange: true);
builder.Configuration.AddJsonFile("messages.pt.json", optional: false, reloadOnChange: true);
builder.Services.AddSingleton<MessagesService>();

// Base de datos SQLite (ruta configurable: sección "Database" o variable DATA_DIR).
builder.Services.AddSnowflakeDatabase();

// Clientes HTTP con nombre (timeouts según el uso).
builder.Services.AddSnowflakeHttpClients();

// Servicios de dominio + bot de Discord.
builder.Services.AddSnowflakeServices();
builder.Services.AddDiscordClient(builder.Configuration);

// CORS: permite que el frontend del panel (otro origen) llame a la API.
builder.Services.AddSnowflakeCors(builder.Configuration);

var app = builder.Build();

// Crea/actualiza la base de datos aplicando las migraciones de EF Core.
{
    await using var db = await app.Services
        .GetRequiredService<IDbContextFactory<BotDbContext>>()
        .CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

// API REST para el portal web de configuración.
app.UseCors(SnowflakeServiceExtensions.CorsPolicyName);
app.MapGet("/api/status", () => Results.Ok(new { status = "online", timestamp = DateTime.UtcNow }));
app.MapConfigEndpoints();
app.MapBotInfoEndpoints();

await app.RunAsync();

// ------------------------- Registros del contenedor -------------------------

public static partial class SnowflakeServiceExtensions
{
    /// <summary>Nombre de la política CORS del panel web.</summary>
    public const string CorsPolicyName = "SnowflakeWeb";

    /// <summary>
    /// CORS para el panel web. Orígenes permitidos configurados en la sección
    /// "Web:AllowedOrigins" de appsettings.json; "*" permite cualquiera
    /// (desarrollo). No se usan cookies, así que no se habilita AllowCredentials.
    /// </summary>
    public static IServiceCollection AddSnowflakeCors(
        this IServiceCollection services, IConfiguration configuration)
    {
        var origenes = configuration.GetSection("Web:AllowedOrigins").Get<string[]>()
            ?? ["*"];

        services.AddCors(o => o.AddPolicy(CorsPolicyName, policy =>
        {
            if (origenes.Contains("*"))
                policy.AllowAnyOrigin();
            else
                policy.WithOrigins(origenes);

            policy.AllowAnyMethod().AllowAnyHeader();
        }));
        return services;
    }

    /// <summary>Enlaza todas las opciones tipadas (secciones de appsettings.json).</summary>
    public static IServiceCollection AddSnowflakeOptions(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BotConfiguration>(configuration.GetSection("Bot"));
        services.Configure<DeepSeekOptions>(configuration.GetSection("DeepSeek"));
        services.Configure<ColorOptions>(configuration.GetSection("Colors"));
        services.Configure<DatabaseOptions>(configuration.GetSection("Database"));
        services.Configure<YouTubeOptions>(configuration.GetSection("YouTube"));
        services.Configure<MusicOptions>(configuration.GetSection("Music"));
        services.Configure<LavalinkOptions>(configuration.GetSection("Lavalink"));
        services.Configure<DownloadOptions>(configuration.GetSection("Downloads"));
        return services;
    }

    /// <summary>Registra la base de datos SQLite y su fábrica de contextos.</summary>
    public static IServiceCollection AddSnowflakeDatabase(this IServiceCollection services)
    {
        var db = new DatabaseOptions();
        // En Fly.io el volumen persistente se monta en DATA_DIR y el archivo
        // puede ir por delante del modelo tras una actualización: se ignora el
        // aviso (las migraciones se aplican al arrancar en Program).
        services.AddDbContextFactory<BotDbContext>(options =>
        {
            options.UseSqlite($"Data Source={db.ResolveFullPath()}");
            options.ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });
        return services;
    }

    /// <summary>
    /// Clientes HTTP con nombre: cada integración externa tiene su propio
    /// timeout y su propio ciclo de vida (gestionado por IHttpClientFactory).
    /// </summary>
    public static IServiceCollection AddSnowflakeHttpClients(this IServiceCollection services)
    {
        // Fallback de canciones de Spotify (oEmbed).
        services.AddHttpClient("Spotify", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SnowflakeBot/1.0");
        });

        // Diagnóstico REST de Lavalink.
        services.AddHttpClient("LavalinkDiag", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SnowflakeBot/1.0");
        });

        // Chatbot DeepSeek: la generación puede tardar varios segundos.
        services.AddHttpClient("DeepSeek", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SnowflakeBot/1.0");
        });

        // Feed RSS público de YouTube.
        services.AddHttpClient("YouTube", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SnowflakeBot/1.0");
        });

        // Subida de archivos grandes a litterbox (puede tardar minutos).
        services.AddHttpClient("Litterbox", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SnowflakeBot/1.0");
        });

        // API de imágenes de gatos (The Cat API / Cataas).
        services.AddHttpClient("CatApi", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SnowflakeBot/1.0");
        });

        return services;
    }

    /// <summary>Servicios de dominio del bot (uno por feature, todos singleton).</summary>
    public static IServiceCollection AddSnowflakeServices(this IServiceCollection services)
    {
        // Ajustes por servidor: punto único de acceso (bot + panel web).
        services.AddSingleton<GuildSettingsService>();
        services.AddSingleton<AiCommandExecutor>();
        services.AddSingleton<AiCommandConfirmation>();
        services.AddSingleton<PrefixCommandService>();

        services.AddSingleton<ModerationLogService>();
        services.AddSingleton<DownloadService>();
        services.AddSingleton<LitterboxService>();
        services.AddSingleton<ColorService>();
        services.AddSingleton<CalculatorService>();
        services.AddSingleton<VoiceHubService>();
        services.AddSingleton<MusicService>();
        services.AddSingleton<MusicWidgetService>();
        services.AddSingleton<CountingService>();
        services.AddSingleton<DeepSeekService>();
        services.AddSingleton<YouTubeNotifyService>();
        services.AddSingleton<ChannelLockService>();
        services.AddSingleton<CatService>();
        services.AddHostedService<YouTubeNotifyService>();
        return services;
    }

    /// <summary>Registra el DiscordClient, el host del bot y el enlace Lavalink.</summary>
    public static IServiceCollection AddDiscordClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Lavalink (música).
        var lavalink = configuration.GetSection("Lavalink").Get<LavalinkOptions>() ?? new LavalinkOptions();
        services.AddLavalink();
        services.ConfigureLavalink(o =>
        {
            o.BaseAddress = new Uri($"http://{lavalink.Host}:{lavalink.Port}");
            o.Passphrase = lavalink.Password;
            o.Label = "snowflake";
        });

        services.AddSingleton(sp =>
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
                        | DiscordIntents.GuildMessages
                        | DiscordIntents.MessageContents,
                LoggerFactory = sp.GetRequiredService<ILoggerFactory>()
            });
        });

        services.AddHostedService<DiscordBotService>();
        return services;
    }
}
