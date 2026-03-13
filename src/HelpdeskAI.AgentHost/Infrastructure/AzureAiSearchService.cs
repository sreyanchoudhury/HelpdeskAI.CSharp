using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Extensions.Options;

namespace HelpdeskAI.AgentHost.Infrastructure;

internal sealed class AzureAiSearchService(
    IOptions<AzureAiSearchSettings> opts,
    ILogger<AzureAiSearchService> log) : IKnowledgeSearch, IKnowledgeIngestion
{
    private const string SemanticConfigName = "helpdesk-semantic-config";

    private readonly SearchClient _client = new(
        new Uri(opts.Value.Endpoint),
        opts.Value.IndexName,
        new AzureKeyCredential(opts.Value.ApiKey));
    private readonly AzureAiSearchSettings _cfg = opts.Value;

    private SearchOptions BuildSearchOptions() => new()
    {
        QueryType = SearchQueryType.Semantic,
        SemanticSearch = new SemanticSearchOptions
        {
            SemanticConfigurationName = SemanticConfigName,
            QueryCaption = new QueryCaption(QueryCaptionType.Extractive) { HighlightEnabled = false }
        },
        Size = _cfg.TopK,
        Select = { "id", "title", "content", "category" }
    };

    public async Task<string?> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        try
        {
            var results = await _client.SearchAsync<SearchDocument>(query, BuildSearchOptions(), ct);
            var chunks = new List<string>();

            await foreach (var r in results.Value.GetResultsAsync().WithCancellation(ct))
            {
                var title   = r.Document.TryGetValue("title",   out var t) ? t?.ToString() : "Untitled";
                var content = r.Document.TryGetValue("content", out var c) ? c?.ToString() : "";
                var snippet = r.SemanticSearch?.Captions?.FirstOrDefault()?.Text
                              ?? (content?.Length > 400 ? content[..400] + "…" : content);
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

    public async Task<string> IndexDocumentAsync(
        string title, string content, string? category = null, CancellationToken ct = default)
    {
        var id  = $"KB-up-{Guid.NewGuid():N}";
        var doc = new SearchDocument
        {
            ["id"]       = id,
            ["title"]    = title,
            ["content"]  = content,
            ["category"] = category ?? "Uploaded",
            ["tags"]     = new[] { "uploaded" }
        };
        try
        {
            await _client.MergeOrUploadDocumentsAsync(new[] { doc }, cancellationToken: ct);
            log.LogInformation("Indexed KB document '{Title}' as '{Id}'", title, id);
            return id;
        }
        catch (RequestFailedException ex)
        {
            log.LogWarning(ex, "Failed to index KB document '{Title}'", title);
            throw;
        }
    }

    public async Task<IReadOnlyList<KbSearchResult>> BrowseLatestAsync(int top = 5, CancellationToken ct = default)
    {
        try
        {
            var options = new SearchOptions
            {
                Size = top,
                Select = { "id", "title", "content", "category" }
            };
            var results = await _client.SearchAsync<SearchDocument>("*", options, ct);
            var items   = new List<KbSearchResult>();

            await foreach (var r in results.Value.GetResultsAsync().WithCancellation(ct))
            {
                var id       = r.Document.TryGetValue("id",       out var ri)  ? ri?.ToString()  ?? ""          : "";
                var title    = r.Document.TryGetValue("title",    out var rt)  ? rt?.ToString()  ?? "Untitled"   : "Untitled";
                var content  = r.Document.TryGetValue("content",  out var rc)  ? rc?.ToString()  ?? ""          : "";
                var category = r.Document.TryGetValue("category", out var rca) ? rca?.ToString()                : null;
                var snippet  = content.Length > 400 ? content[..400] + "\u2026" : content;
                items.Add(new KbSearchResult(id, title, snippet, category));
            }

            return items;
        }
        catch (RequestFailedException ex)
        {
            log.LogWarning(ex, "AI Search browse failed");
            return [];
        }
    }

    public async Task<IReadOnlyList<KbSearchResult>> SearchStructuredAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        try
        {
            var results = await _client.SearchAsync<SearchDocument>(query, BuildSearchOptions(), ct);
            var items   = new List<KbSearchResult>();

            await foreach (var r in results.Value.GetResultsAsync().WithCancellation(ct))
            {
                var id       = r.Document.TryGetValue("id",       out var ri)  ? ri?.ToString()  ?? ""        : "";
                var title    = r.Document.TryGetValue("title",    out var rt)  ? rt?.ToString()  ?? "Untitled" : "Untitled";
                var content  = r.Document.TryGetValue("content",  out var rc)  ? rc?.ToString()  ?? ""        : "";
                var category = r.Document.TryGetValue("category", out var rca) ? rca?.ToString()              : null;
                var snippet  = r.SemanticSearch?.Captions?.FirstOrDefault()?.Text
                               ?? (content.Length > 400 ? content[..400] + "…" : content);
                items.Add(new KbSearchResult(id, title, snippet, category));
            }

            return items;
        }
        catch (RequestFailedException ex)
        {
            log.LogWarning(ex, "AI Search structured failed for '{Query}'", query);
            return [];
        }
    }
}

