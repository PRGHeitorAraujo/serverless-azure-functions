using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ServerlessAzure.FunctionApp.Blob;

/// <summary>
/// Blob Trigger + Blob Output (equivalente ao fn-intput-blob do projeto original).
/// Quando um arquivo de texto chega no container "uploads", gera um relatório JSON
/// com estatísticas do conteúdo no container "processed".
/// </summary>
public class ProcessUploadedFile(ILogger<ProcessUploadedFile> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [Function("ProcessUploadedFile")]
    [BlobOutput("processed/{name}.json", Connection = "StorageConnection")]
    public string Run(
        [BlobTrigger("uploads/{name}", Connection = "StorageConnection")] string content,
        string name)
    {
        logger.LogInformation("Processando blob {Name} ({Length} caracteres)", name, content.Length);

        var lines = content.Split('\n');
        var words = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var report = new BlobReport(
            FileName: name,
            Characters: content.Length,
            Lines: content.Length == 0 ? 0 : lines.Length,
            Words: words.Length,
            Preview: content.Length > 200 ? content[..200] + "…" : content,
            ProcessedAtUtc: DateTime.UtcNow);

        return JsonSerializer.Serialize(report, JsonOptions);
    }
}

public record BlobReport(
    string FileName,
    int Characters,
    int Lines,
    int Words,
    string Preview,
    DateTime ProcessedAtUtc);
