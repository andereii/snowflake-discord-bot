namespace Snowflake.Bot.Data.Entities;

/// <summary>
/// Estadísticas y ranking de un usuario en el juego de trivia de un servidor.
/// </summary>
public sealed class TriviaStat
{
    public int Id { get; set; }
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }

    /// <summary>Puntos acumulados.</summary>
    public int Score { get; set; }

    /// <summary>Preguntas respondidas correctamente.</summary>
    public int CorrectAnswers { get; set; }

    /// <summary>Total de preguntas intentadas.</summary>
    public int TotalAnswers { get; set; }

    /// <summary>Racha actual de respuestas correctas consecutivas.</summary>
    public int CurrentStreak { get; set; }

    /// <summary>Mejor racha histórica de respuestas correctas consecutivas.</summary>
    public int BestStreak { get; set; }

    /// <summary>Fecha y hora de la última partida jugada.</summary>
    public DateTimeOffset LastPlayedAt { get; set; } = DateTimeOffset.UtcNow;
}
