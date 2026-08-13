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
    public string ResolveFullPath()
    {
        // En despliegues con volumen persistente (Fly.io), DATA_DIR apunta al
        // volumen montado. Tiene prioridad sobre cualquier otra ruta.
        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR");
        if (!string.IsNullOrWhiteSpace(dataDir))
            return System.IO.Path.Combine(dataDir, System.IO.Path.GetFileName(Path));

        return System.IO.Path.IsPathRooted(Path)
            ? Path
            : System.IO.Path.Combine(AppContext.BaseDirectory, Path);
    }
}
