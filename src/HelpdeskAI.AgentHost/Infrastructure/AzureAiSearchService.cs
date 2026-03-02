using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Extensions.Options;

namespace HelpdeskAI.AgentHost.Infrastructure;

internal sealed class AzureAiSearchService(
    IOptions<AzureAiSearchSettings> opts,
    ILogger<AzureAiSearchService> log) : IKnowledgeSearch
{
    private readonly SearchClient _client = new(
        new Uri(opts.Value.Endpoint),
        opts.Value.IndexName,
        new AzureKeyCredential(opts.Value.ApiKey));
    private readonly AzureAiSearchSettings _cfg = opts.Value;

    public async Task<string?> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        try
        {
            var options = new SearchOptions
            {
                QueryType = SearchQueryType.Semantic,
                SemanticSearch = new SemanticSearchOptions
                {
                    SemanticConfigurationName = "helpdesk-semantic-config",
                    QueryCaption = new QueryCaption(QueryCaptionType.Extractive) { HighlightEnabled = false }
                },
                Size = _cfg.TopK,
                Select = { "id", "title", "content", "category" }
            };

            var results = await _client.SearchAsync<SearchDocument>(query, options, ct);
            var chunks = new List<string>();

            await foreach (var r in results.Value.GetResultsAsync().WithCancellation(ct))
            {
                var title   = r.Document.TryGetValue("title",   out var t) ? t?.ToString() : "Untitled";
                var content = r.Document.TryGetValue("content", out var c) ? c?.ToString() : "";
                var snippet = r.SemanticSearch?.Captions?.FirstOrDefault()?.Text
                              ?? (content?.Length > 400 ? content[..400] + "" : content);
                chunks.Add($"### {title}\n{snippet}");
            }

            return chunks.Count == 0 ? null
                : "## Relevant IT Knowledge Base Articles\n\n" + string.Join("\n\n---\n\n", chunks);
        }
        catch (RequestFailedException ex)
        {
            log.LogWarning(ex, "AI Search failed for '{Query}'", query);
            return null;
        }
    }
}


