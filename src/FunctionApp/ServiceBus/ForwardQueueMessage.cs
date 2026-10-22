using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ServerlessAzure.FunctionApp.ServiceBus;

/// <summary>
/// Service Bus Trigger (equivalente ao fn-ler-sb do projeto original).
/// Lê mensagens JSON da fila e as encaminha via POST para um webhook configurável.
///
/// Diferenças em relação ao original:
///  - URL e conexão vêm da configuração (no original estavam vazias no código);
///  - em caso de falha a mensagem é "abandonada" (volta pra fila e é reprocessada,
///    indo para a dead-letter queue após MaxDeliveryCount) em vez de "deferred",
///    que deixaria a mensagem presa até alguém buscá-la pelo sequence number;
///  - mensagens que nunca vão funcionar (sem webhook configurado) vão direto pra DLQ.
/// </summary>
public class ForwardQueueMessage(
    ILogger<ForwardQueueMessage> logger,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration)
{
    public const string HttpClientName = "webhook";

    [Function("ForwardQueueMessage")]
    public async Task Run(
        [ServiceBusTrigger("%ServiceBusQueueName%", Connection = "ServiceBusConnection", AutoCompleteMessages = false)]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Mensagem {MessageId} recebida (tentativa {DeliveryCount}, content-type {ContentType})",
            message.MessageId, message.DeliveryCount, message.ContentType);

        string? webhookUrl = configuration["WebhookUrl"];
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            logger.LogError("WebhookUrl não configurada; enviando mensagem {MessageId} para a DLQ", message.MessageId);
            await messageActions.DeadLetterMessageAsync(
                message,
                deadLetterReason: "ConfigurationError",
                deadLetterErrorDescription: "WebhookUrl não configurada",
                cancellationToken: cancellationToken);
            return;
        }

        try
        {
            using var content = new StringContent(message.Body.ToString(), Encoding.UTF8, "application/json");
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsync(webhookUrl, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Mensagem {MessageId} encaminhada com sucesso ({Status})",
                    message.MessageId, (int)response.StatusCode);
                await messageActions.CompleteMessageAsync(message, cancellationToken);
                return;
            }

            logger.LogWarning("Webhook respondeu {Status} para a mensagem {MessageId}; abandonando para nova tentativa",
                (int)response.StatusCode, message.MessageId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Falha de rede ao encaminhar a mensagem {MessageId}; abandonando para nova tentativa",
                message.MessageId);
        }

        await messageActions.AbandonMessageAsync(message, cancellationToken: cancellationToken);
    }
}
