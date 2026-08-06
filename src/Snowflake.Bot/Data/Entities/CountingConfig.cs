namespace Snowflake.Bot.Data.Entities;

/// <summary>Base numérica del modo de juego de conteo.</summary>
public enum CountingBase
{
    Decimal,
    Binario,
    Octal,
    Hexadecimal
}

/// <summary>
/// Configuración del juego de conteo por servidor.
/// </summary>
public sealed class CountingConfig
{
    /// <summary>Id del servidor (clave primaria).</summary>
    public ulong GuildId { get; set; }

    /// <summary>Canal donde se cuenta. Null = desactivado.</summary>
    public ulong? ChannelId { get; set; }

    /// <summary>Valor actual de la cadena (almacenado en decimal).</summary>
    public long CurrentValue { get; set; }

    /// <summary>Último usuario que contó (no puede contar dos veces seguidas).</summary>
    public ulong? LastUserId { get; set; }

    /// <summary>Récord histórico del servidor (mayor cuenta alcanzada jamás).</summary>
    public long CurrentRecord { get; set; }

    /// <summary>Récord que había al empezar la cadena actual (para detectar nuevos récords reales).</summary>
    public long RecordAtChainStart { get; set; }

    /// <summary>Si ya se celebró un récord durante esta cadena (evita spam).</summary>
    public bool RecordCelebratedThisChain { get; set; }

    /// <summary>Base en la que se cuenta (decimal, binario, etc.).</summary>
    public CountingBase Base { get; set; } = CountingBase.Decimal;

    /// <summary>Objetivo del servidor (null = sin objetivo).</summary>
    public long? Goal { get; set; }

    /// <summary>Oportunidades extra diarias (0-10, 0 = desactivado).</summary>
    public int ExtraChancesPerDay { get; set; }

    /// <summary>Oportunidades usadas hoy.</summary>
    public int ExtraChancesUsedToday { get; set; }

    /// <summary>Fecha (UTC, "yyyy-MM-dd") del último reseteo de oportunidades.</summary>
    public string? LastExtraChanceResetDate { get; set; }

    /// <summary>Icono de respuesta correcta (null = ✅).</summary>
    public string? EmojiCorrect { get; set; }

    /// <summary>Icono de respuesta incorrecta (null = ❌).</summary>
    public string? EmojiIncorrect { get; set; }

    /// <summary>Icono de nuevo récord (null = 🎉).</summary>
    public string? EmojiRecord { get; set; }

    /// <summary>Mensaje personalizado al perder la cuenta (placeholders {cuenta} {usuario} {siguiente}). Null = por defecto.</summary>
    public string? LoseMessage { get; set; }
}

/// <summary>
/// Estadísticas de un usuario en el juego de conteo de un servidor.
/// </summary>
public sealed class CountingStat
{
    public int Id { get; set; }
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }

    /// <summary>Números contados correctamente.</summary>
    public long TotalCounts { get; set; }

    /// <summary>Intentos fallidos (errores o mismo-usuario-dos-veces).</summary>
    public long IncorrectCounts { get; set; }

    /// <summary>Mayor número que este usuario aportó correctamente.</summary>
    public long BestContribution { get; set; }
}