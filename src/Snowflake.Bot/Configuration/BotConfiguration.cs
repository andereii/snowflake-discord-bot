namespace Snowflake.Bot.Configuration;

/// <summary>
/// Configuración general del bot, enlazada desde la sección "Bot" de appsettings.json.
/// </summary>
public sealed class BotConfiguration
{
    /// <summary>
    /// Servidor de pruebas donde se registran los comandos slash (aparecen al instante).
    /// </summary>
    public ulong TestGuildId { get; set; }

    /// <summary>
    /// Id del dueño del bot, para comandos administrativos.
    /// </summary>
    public ulong OwnerId { get; set; }

    /// <summary>
    /// Modo debug del bot. Cuando es true, los mensajes de error que ve el
    /// usuario incluyen detalles técnicos (útil durante el desarrollo).
    /// En false (producción) los errores son genéricos para no filtrar información.
    /// Se lee en caliente desde appsettings.json.
    /// </summary>
    public bool Debug { get; set; }
}
