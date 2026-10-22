using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Sql;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ServerlessAzure.FunctionApp.Sql;

/// <summary>
/// CRUD simples de tarefas no Azure SQL usando os bindings de SQL
/// (o original, fn-save-sql, tinha só o POST).
/// </summary>
public class ToDoFunctions(ILogger<ToDoFunctions> logger, IConfiguration configuration)
{
    private const string SelectColumns = "SELECT Id, [Order], Title, Url, Completed FROM dbo.ToDo";

    // GET /api/todos
    [Function("ListToDos")]
    public IActionResult List(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "todos")] HttpRequest req,
        [SqlInput(SelectColumns + " ORDER BY [Order]", "SqlConnectionString")] IEnumerable<ToDoItem> todos)
        => new OkObjectResult(todos);

    // GET /api/todos/{id}
    [Function("GetToDo")]
    public IActionResult GetById(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "todos/{id:guid}")] HttpRequest req,
        [SqlInput(SelectColumns + " WHERE Id = @Id", "SqlConnectionString", parameters: "@Id={id}")] IEnumerable<ToDoItem> result)
    {
        var item = result.FirstOrDefault();
        return item is null ? new NotFoundResult() : new OkObjectResult(item);
    }

    // POST /api/todos  { "title": "Estudar Azure Functions", "order": 1 }
    [Function("CreateToDo")]
    public async Task<CreateToDoOutput> Create(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "todos")] HttpRequest req)
    {
        CreateToDoRequest? body;
        try
        {
            body = await req.ReadFromJsonAsync<CreateToDoRequest>();
        }
        catch (JsonException)
        {
            return CreateToDoOutput.BadRequest("JSON inválido.");
        }

        if (string.IsNullOrWhiteSpace(body?.Title))
        {
            return CreateToDoOutput.BadRequest("O campo 'title' é obrigatório.");
        }

        if (body.Title.Length > 200)
        {
            return CreateToDoOutput.BadRequest("O campo 'title' aceita no máximo 200 caracteres.");
        }

        var item = new ToDoItem
        {
            Id = Guid.NewGuid(),
            Title = body.Title.Trim(),
            Order = body.Order,
            Completed = body.Completed ?? false
        };

        // No original a URL era fixa ("google.com/?id=..."); aqui vem da configuração.
        string baseUrl = configuration["ToDoBaseUrl"]?.TrimEnd('/') ?? "/api/todos";
        item.Url = $"{baseUrl}/{item.Id}";

        logger.LogInformation("Criando tarefa {Id}: {Title}", item.Id, item.Title);

        return new CreateToDoOutput
        {
            ToDoItem = item,
            HttpResponse = new CreatedResult(item.Url, item)
        };
    }
}

/// <summary>
/// Saída múltipla: a resposta HTTP + a linha a ser gravada no SQL.
/// Se ToDoItem for null (erro de validação), nada é gravado.
/// </summary>
public class CreateToDoOutput
{
    [SqlOutput("dbo.ToDo", connectionStringSetting: "SqlConnectionString")]
    public ToDoItem? ToDoItem { get; set; }

    [HttpResult]
    public IActionResult HttpResponse { get; set; } = new OkResult();

    public static CreateToDoOutput BadRequest(string error)
        => new() { HttpResponse = new BadRequestObjectResult(new { error }) };
}
