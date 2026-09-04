using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Snowflake.Bot.Services;

/// <summary>
/// Tarea en segundo plano que publica las felicitaciones de cumpleaños
/// a la hora configurada en cada servidor.
/// </summary>
public sealed class BirthdayNotifierService(
    BirthdayService birthdays,
    ILogger<BirthdayNotifierService> logger) : BackgroundService
{
    private static readonly TimeSpan Periodo = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BirthdayNotifierService iniciado (chequeo cada 1 h).");

        // Esperar a que el bot esté listo antes del primer chequeo.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Periodo);
        do
        {
            try
            {
                await birthdays.PublicarCumplesDelDiaAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en BirthdayNotifierService");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
