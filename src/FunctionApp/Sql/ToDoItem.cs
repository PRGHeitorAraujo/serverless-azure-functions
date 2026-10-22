namespace ServerlessAzure.FunctionApp.Sql;

/// <summary>
/// Linha da tabela dbo.ToDo. Os nomes das propriedades batem exatamente com as colunas,
/// que é como o binding de SQL faz o mapeamento.
/// </summary>
public class ToDoItem
{
    public Guid Id { get; set; }
    public int? Order { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public bool Completed { get; set; }
}

/// <summary>Payload aceito no POST /api/todos.</summary>
public record CreateToDoRequest(string? Title, int? Order, bool? Completed);
