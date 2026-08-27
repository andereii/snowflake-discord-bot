namespace Snowflake.Bot.Utilities;

/// <summary>
/// Da formato a las respuestas del chatbot respetando el límite de Discord.
/// </summary>
public static class ChatResponseFormatter
{
    private const int MaxDiscordMessageLength = 2000;

    /// <summary>
    /// Devuelve únicamente la respuesta de la IA, recortada si es necesario.
    /// La nota de truncado se localiza con la clave "Chat:Truncada".
    /// </summary>
    public static string Formatear(string respuesta, string notaTruncada = "\n\n*(response truncated)*")
    {
        if (respuesta.Length <= MaxDiscordMessageLength)
            return respuesta;

        return Truncar(respuesta, MaxDiscordMessageLength - notaTruncada.Length) + notaTruncada;
    }

    private static string Truncar(string texto, int max)
    {
        if (max <= 0) return string.Empty;
        if (texto.Length <= max) return texto;

        const string elipsis = "…";
        var recortado = max - elipsis.Length;
        return recortado <= 0 ? texto[..max] : texto[..recortado] + elipsis;
    }
}
