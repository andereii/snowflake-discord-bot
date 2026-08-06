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
    /// Id del dueño del bot. Reservado para la autenticación del futuro panel
    /// web de configuración (solo el dueño podrá editar ajustes globales).
    /// </summary>
    public ulong OwnerId { get; set; }

    /// <summary>
    /// Modo debug del bot. Cuando es true, los mensajes de error que ve el
    /// usuario incluyen detalles técnicos (útil durante el desarrollo).
    /// En false (producción) los errores son genéricos para no filtrar información.
    /// Se lee en caliente desde appsettings.json.
    /// </summary>
    public bool Debug { get; set; }

    /// <summary>
    /// Segundos que una entrada de la caché de ajustes por servidor se considera
    /// válida antes de releerse de la base de datos. Importante cuando un panel
    /// web externo escribe directamente en la BD: sus cambios tardan como máximo
    /// este tiempo en verse reflejados en el bot.
    /// </summary>
    public int SettingsCacheSeconds { get; set; } = 60;
}
