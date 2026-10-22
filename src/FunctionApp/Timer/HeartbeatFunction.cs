using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ServerlessAzure.FunctionApp.Timer;

/// <summary>
/// Timer Trigger (equivalente ao fn-tempo do projeto original).
/// O agendamento vem da configuração "TimerSchedule" (CRON de 6 campos),
/// então dá pra mudar a frequência sem recompilar.
/// </summary>
public class HeartbeatFunction(ILogger<HeartbeatFunction> logger)
{
    [Function("Heartbeat")]
    public void Run([TimerTrigger("%TimerSchedule%")] TimerInfo timer)
    {
        logger.LogInformation("Heartbeat executado em {NowUtc:O}", DateTime.UtcNow);

        if (timer.IsPastDue)
        {
            logger.LogWarning("Execução atrasada (past due) — o host ficou indisponível no horário agendado.");
        }

        if (timer.ScheduleStatus is not null)
        {
            logger.LogInformation("Próxima execução: {Next:O}", timer.ScheduleStatus.Next);
        }
    }
}
