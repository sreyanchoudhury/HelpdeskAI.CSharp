using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Agents;

/// <summary>
/// Per-turn AIContextProvider that injects only the most relevant MCP tools into the LLM context.
/// Tool descriptions are embedded once at startup (initialised via ApplicationStarted to avoid
/// blocking the health probe); only the user's query is embedded each turn, then cosine similarity
/// selects the top-K tools.
/// </summary>
internal sealed class DynamicToolSelectionProvider : AIContextProvider
{
    // Resolved after server starts — awaited with a timeout on the first chat turn.
    private readonly Task<IReadOnlyList<(AIFunction Tool, float[] Vector)>> _toolIndexTask;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embedder;
    private readonly int _topK;
    private readonly ILogger<DynamicToolSelectionProvider> _logger;

    // Cache tool selection per unique user query so we embed + rank exactly once per turn.
    // Bounded to 200 entries; cleared when full to prevent unbounded growth.
    private readonly ConcurrentDictionary<string, AIContext> _turnCache = new(StringComparer.Ordinal);

    public DynamicToolSelectionProvider(
        Task<IReadOnlyList<(AIFunction Tool, float[] Vector)>> toolIndexTask,
        IEmbeddingGenerator<string, Embedding<float>> embedder,
        int topK,
        ILogger<DynamicToolSelectionProvider> logger)
    {
        _toolIndexTask = toolIndexTask;
        _embedder = embedder;
        _topK = topK;
        _logger = logger;
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        AIContextProvider.InvokingContext context, CancellationToken ct)
    {
        // Await the index — it resolves quickly after the first request if startup
        // init has already completed; 60 s timeout guards against init failure.
        IReadOnlyList<(AIFunction Tool, float[] Vector)> toolIndex;
        try
        {
            toolIndex = await _toolIndexTask.WaitAsync(TimeSpan.FromSeconds(60), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool index not ready — returning empty tool list");
            return new AIContext { Tools = [] };
        }

        // If the total tool count is at or below topK, ranking adds nothing — skip the
        // embedding API call entirely and return all tools directly.
        if (toolIndex.Count <= _topK)
        {
            _logger.LogDebug("Tool count {Count} <= topK {TopK} — returning all tools without embedding", toolIndex.Count, _topK);
            return new AIContext { Tools = [.. toolIndex.Select(x => x.Tool).Cast<AITool>()] };
        }

        var query = context.AIContext.Messages?
            .LastOrDefault(m => m.Role == ChatRole.User)?.Text;

        if (string.IsNullOrWhiteSpace(query))
        {
            _logger.LogDebug("No user message — injecting all {Count} tools", toolIndex.Count);
            return new AIContext { Tools = [.. toolIndex.Select(x => x.Tool).Cast<AITool>()] };
        }

        // Return cached selection if this query was already resolved this session.
        if (_turnCache.TryGetValue(query, out var cachedContext))
        {
            _logger.LogDebug("Tool selection cache hit — reusing selection for [{Query}]", query);
            return cachedContext;
        }

        try
        {
            // Use an independent timeout so a cancelled request CT from a previous dropped
            // SSE connection does not prevent tool selection on the next turn.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            var queryVec = (await _embedder.GenerateAsync([query], cancellationToken: cts.Token))
                [0].Vector.ToArray();

            var selected = toolIndex
                .Select(x => (x.Tool, Score: Cosine(queryVec, x.Vector)))
                .OrderByDescending(x => x.Score)
                .Take(_topK)
                .Select(x => x.Tool)
                .ToList();

            _logger.LogInformation("Dynamic tools selected for [{Query}]: {Tools}",
                query, string.Join(", ", selected.Select(t => t.Name)));

            var result = new AIContext { Tools = [.. selected.Cast<AITool>()] };

            // Bound the cache to prevent unbounded growth across long sessions.
            if (_turnCache.Count >= 200) _turnCache.Clear();
            _turnCache[query] = result;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding failed — falling back to all {Count} tools", toolIndex.Count);
            return new AIContext { Tools = [.. toolIndex.Select(x => x.Tool).Cast<AITool>()] };
        }
    }

    private static float Cosine(float[] a, float[] b)
    {
        var dot = a.Zip(b, (x, y) => x * y).Sum();
        var magA = MathF.Sqrt(a.Sum(x => x * x));
        var magB = MathF.Sqrt(b.Sum(x => x * x));
        return magA == 0 || magB == 0 ? 0f : dot / (magA * magB);
    }
}
