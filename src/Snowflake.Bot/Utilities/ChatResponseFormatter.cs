namespace Snowflake.Bot.Utilities;

/// <summary>
/// Da formato a las respuestas del chatbot respetando el límite de Discord.
/// </summary>
public static class ChatResponseFormatter
{
    private const int MaxDiscordMessageLength = 2000;

    /// <summary>Devuelve únicamente la respuesta de Gemini, recortada si es necesario.</summary>
    public static string Formatear(string respuesta)
    {
        if (respuesta.Length <= MaxDiscordMessageLength)
            return respuesta;

        const string nota = "\n\n*(respuesta truncada)*";
        return Truncar(respuesta, MaxDiscordMessageLength - nota.Length) + nota;
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
