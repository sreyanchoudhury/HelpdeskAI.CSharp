using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;

namespace HelpdeskAI.McpServer.Services;

public sealed class KnowledgeBaseSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string IndexName { get; set; } = "helpdesk-kb";
    public int TopK { get; set; } = 3;
}

public sealed class KnowledgeBaseService(
    IOptions<KnowledgeBaseSettings> opts,
    ILogger<KnowledgeBaseService> log)
{
    private readonly SearchClient _client = new(
        new Uri(opts.Value.Endpoint),
        opts.Value.IndexName,
        new AzureKeyCredential(opts.Value.ApiKey),
        new Azure.Search.Documents.SearchClientOptions
        {
            // Retry up to 3 times with exponential back-off on transient 429/503/504.
            Retry = { MaxRetries = 3, Mode = Azure.Core.RetryMode.Exponential,
                      Delay = TimeSpan.FromMilliseconds(300), MaxDelay = TimeSpan.FromSeconds(5) }
        });

    public async Task<string> IndexArticleAsync(
        string title, string content, string? category, CancellationToken ct = default)
    {
        var id = $"KB-up-{Guid.NewGuid():N}";
        var doc = new SearchDocument
        {
            ["id"] = id,
            ["title"] = title,
            ["content"] = content,
            ["category"] = category ?? "Uploaded",
            ["tags"] = new[] { "agent-indexed" }
        };

        await _client.MergeOrUploadDocumentsAsync(new[] { doc }, cancellationToken: ct);
        log.LogInformation("KB article '{Title}' indexed with id '{Id}'", title, id);
        return id;
    }
}
