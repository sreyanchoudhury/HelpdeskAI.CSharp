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

    public sealed record KbArticle(
        string Id,
        string Title,
        string Content,
        string? Category,
        double SearchScore,
        string MatchQuality);

    public sealed record IndexArticleResult(
        string Id,
        bool Created,
        bool Updated,
        string Message,
        string Disposition,
        string MatchQuality);

    public async Task<IReadOnlyList<KbArticle>> SearchAsync(
        string query, string? category = null, int topK = 5, CancellationToken ct = default)
    {
        var options = new SearchOptions
        {
            Size   = topK,
            Select = { "id", "title", "content", "category" },
            QueryType = SearchQueryType.Simple,
        };
        if (category is { Length: > 0 })
            options.Filter = $"category eq '{category}'";

        var response = await _client.SearchAsync<SearchDocument>(query, options, ct);
        var results = new List<KbArticle>();
        await foreach (var result in response.Value.GetResultsAsync())
        {
            var doc = result.Document;
            var title = doc["title"]?.ToString() ?? string.Empty;
            var content = doc["content"]?.ToString() ?? string.Empty;
            var score = result.Score ?? 0;
            results.Add(new KbArticle(
                Id:       doc["id"]?.ToString()       ?? string.Empty,
                Title:    title,
                Content:  content,
                Category: doc["category"]?.ToString(),
                SearchScore: score,
                MatchQuality: ClassifyMatchQuality(query, title, content, score)));
        }
        return results;
    }

    public async Task<IndexArticleResult> IndexArticleAsync(
        string title, string content, string? category, CancellationToken ct = default)
    {
        var normalizedTitle = Normalize(title);
        var normalizedContent = Normalize(content);
        var normalizedCategory = Normalize(category);

        var candidates = await SearchAsync(title, category, topK: 10, ct);
        var exactMatch = candidates.FirstOrDefault(article =>
            Normalize(article.Title) == normalizedTitle &&
            Normalize(article.Content) == normalizedContent &&
            Normalize(article.Category) == normalizedCategory);

        if (exactMatch is not null)
        {
            log.LogInformation("KB article '{Title}' already exists as '{Id}' - reusing existing article", title, exactMatch.Id);
            return new IndexArticleResult(
                exactMatch.Id,
                Created: false,
                Updated: false,
                Message: $"KB article already existed. Reusing article ID: {exactMatch.Id}",
                Disposition: "reused",
                MatchQuality: "exact");
        }

        // Only consider agent-indexed articles for refresh; never overwrite seeded KB articles.
        var sameTopic = candidates.FirstOrDefault(article =>
            article.Id.StartsWith("KB-up-", StringComparison.OrdinalIgnoreCase) &&
            Normalize(article.Category) == normalizedCategory &&
            AreTopicsSimilar(normalizedTitle, Normalize(article.Title), normalizedContent, Normalize(article.Content)));

        if (sameTopic is not null)
        {
            var refreshedDoc = new SearchDocument
            {
                ["id"] = sameTopic.Id,
                ["title"] = title,
                ["content"] = content,
                ["category"] = category ?? "Uploaded",
                ["tags"] = new[] { "agent-indexed", "agent-refreshed" },
                ["indexedAt"] = DateTimeOffset.UtcNow
            };

            await _client.MergeOrUploadDocumentsAsync(new[] { refreshedDoc }, cancellationToken: ct);
            log.LogInformation("KB article '{Title}' refreshed existing id '{Id}'", title, sameTopic.Id);
            return new IndexArticleResult(
                sameTopic.Id,
                Created: false,
                Updated: true,
                Message: $"KB article already existed for this topic. Refreshed article ID: {sameTopic.Id}",
                Disposition: "refreshed",
                MatchQuality: sameTopic.MatchQuality);
        }

        var id = $"KB-up-{Guid.NewGuid():N}";
        var doc = new SearchDocument
        {
            ["id"] = id,
            ["title"] = title,
            ["content"] = content,
            ["category"] = category ?? "Uploaded",
            ["tags"] = new[] { "agent-indexed" },
            ["indexedAt"] = DateTimeOffset.UtcNow
        };

        await _client.MergeOrUploadDocumentsAsync(new[] { doc }, cancellationToken: ct);
        log.LogInformation("KB article '{Title}' indexed with id '{Id}'", title, id);
        return new IndexArticleResult(
            id,
            Created: true,
            Updated: false,
            Message: $"Successfully indexed. KB article ID: {id}",
            Disposition: "created",
            MatchQuality: "new");
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var parts = value
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).ToLowerInvariant();
    }

    private static string ClassifyMatchQuality(string query, string title, string content, double score)
    {
        var normalizedQuery = Normalize(query);
        var normalizedTitle = Normalize(title);
        var normalizedContent = Normalize(content);

        if (normalizedQuery.Length > 0 &&
            (normalizedTitle.Contains(normalizedQuery, StringComparison.Ordinal) ||
             normalizedContent.Contains(normalizedQuery, StringComparison.Ordinal)))
        {
            return "strong";
        }

        return score >= 1.5 ? "strong" : score >= 0.8 ? "related" : "weak";
    }

    private static bool AreTopicsSimilar(
        string normalizedTitle,
        string candidateTitle,
        string normalizedContent,
        string candidateContent)
    {
        if (normalizedTitle == candidateTitle)
            return true;

        var titleOverlap = CalculateTokenOverlap(normalizedTitle, candidateTitle);
        var contentOverlap = CalculateTokenOverlap(normalizedContent, candidateContent);
        return titleOverlap >= 0.6 || (titleOverlap >= 0.4 && contentOverlap >= 0.35);
    }

    private static double CalculateTokenOverlap(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return 0;

        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
            return 0;

        var intersection = leftTokens.Intersect(rightTokens, StringComparer.Ordinal).Count();
        var denominator = Math.Max(leftTokens.Count, rightTokens.Count);
        return denominator == 0 ? 0 : (double)intersection / denominator;
    }
}
