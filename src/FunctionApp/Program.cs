using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServerlessAzure.FunctionApp.ServiceBus;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // HttpClient nomeado (via IHttpClientFactory) para o encaminhamento de mensagens do Service Bus.
        services.AddHttpClient(ForwardQueueMessage.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });
    })
    .Build();

host.Run();
