namespace Snowflake.Bot.Configuration;

/// <summary>
/// Opciones del módulo de música. Sección "Music" de appsettings.json.
/// </summary>
public sealed class MusicOptions
{
    /// <summary>
    /// Segundos que el widget "reproduciendo ahora" permanece visible tras
    /// detener la música antes de borrarse automáticamente.
    /// </summary>
    public int WidgetDeleteDelaySeconds { get; set; } = 5;

    /// <summary>Volumen mínimo permitido (porcentaje).</summary>
    public int MinVolume { get; set; } = 0;

    /// <summary>Volumen máximo permitido (porcentaje).</summary>
    public int MaxVolume { get; set; } = 100;
}
