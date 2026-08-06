namespace Snowflake.Bot.Configuration;

/// <summary>
/// Ruta de la base de datos SQLite. Sección "Database" de appsettings.json.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>
    /// Nombre o ruta del archivo .db. Si es relativo, se resuelve junto al
    /// ejecutable (AppContext.BaseDirectory), para que la BD siempre acompañe
    /// al binario sin importar desde qué directorio se lance el bot.
    /// </summary>
    public string Path { get; set; } = "snowflake.db";

    /// <summary>Ruta absoluta efectiva del archivo de base de datos.</summary>
    public string ResolveFullPath() =>
        System.IO.Path.IsPathRooted(Path)
            ? Path
            : System.IO.Path.Combine(AppContext.BaseDirectory, Path);
}
