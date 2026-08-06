namespace Snowflake.Bot.Utilities;

/// <summary>
/// Emojis personalizados de la aplicación (subidos desde el Developer Portal).
/// Al ser emojis de aplicación, el bot puede usarlos en cualquier servidor.
/// Los textos de messages.json ya los llevan incrustados; estas constantes son
/// para cuando el código necesita componer un mensaje fuera de messages.json.
/// </summary>
public static class BotEmojis
{
    /// <summary>✅ Marca de éxito.</summary>
    public const string Check = "<:check:1534413756057260152>";

    /// <summary>❌ Marca de error.</summary>
    public const string Error = "<:error:1534417252185800720>";

    /// <summary>Indicador animado de "la IA está pensando".</summary>
    public const string Load = "<a:load:1534442024281837749>";

    /// <summary>Indicador animado de carga/espera.</summary>
    public const string LoadingWindows = "<a:loadingwindows:1534386932744716328>";

    /// <summary>❄️ Marca del bot.</summary>
    public const string Snowflake = "<:snowflake:1534441299795771433>";
}
