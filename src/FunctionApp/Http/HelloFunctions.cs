using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ServerlessAzure.FunctionApp.Http;

/// <summary>
/// HTTP Trigger básico (equivalente ao fn-simples do projeto original).
/// </summary>
public class HelloFunctions(ILogger<HelloFunctions> logger)
{
    // GET /api/hello?name=Heitor
    [Function("Hello")]
    public IActionResult Hello(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "hello")] HttpRequest req)
    {
        string name = req.Query["name"].FirstOrDefault() is { Length: > 0 } n ? n : "dev";
        logger.LogInformation("Hello chamado para {Name}", name);

        return new OkObjectResult(new
        {
            message = $"Olá, {name}! Bem-vindo ao Azure Functions.",
            timestampUtc = DateTime.UtcNow
        });
    }

    // GET /api/health — usado para smoke test após o deploy
    [Function("Health")]
    public IActionResult Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req)
        => new OkObjectResult(new { status = "healthy", timestampUtc = DateTime.UtcNow });
}
