using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HelpdeskAI.AgentHost.Infrastructure;

/// <summary>
/// Intercepts chat client calls to capture token usage from the LLM response
/// and write it directly to Redis while the streaming response is still open.
/// Writing inside the generator (before the HTTP response closes) avoids a race
/// condition where the frontend fetches /agent/usage before StoreChatHistoryAsync runs.
/// </summary>
internal sealed class UsageCapturingChatClient(
    IChatClient inner,
    IRedisService redis,
    IOptions<ConversationSettings> settings) : DelegatingChatClient(inner)
{
    private readonly ConversationSettings _settings = settings.Value;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        if (response.Usage is { } u)
            await PersistUsageAsync(u.InputTokenCount ?? 0, u.OutputTokenCount ?? 0);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            updates.Add(update);
            yield return update;
        }

        // Aggregate streaming chunks into ChatResponse to extract usage.
        // Azure OpenAI includes usage in the last streaming chunk, which MEAI maps here.
        // Writing to Redis here (before the generator returns) ensures the key exists
        // by the time the frontend fetches /agent/usage after isLoading goes false.
        var aggregated = updates.ToChatResponse();
        if (aggregated.Usage is { } u)
            await PersistUsageAsync(u.InputTokenCount ?? 0, u.OutputTokenCount ?? 0);
    }

    // 2-minute TTL for the global fallback key — short enough to reflect only the
    // most recent response, long enough to survive the frontend polling window.
    private static readonly TimeSpan UsageLatestTtl = TimeSpan.FromMinutes(2);

    private async Task PersistUsageAsync(long promptTokens, long completionTokens)
    {
        var json = JsonSerializer.Serialize(new { promptTokens, completionTokens });
        var tid = ThreadIdContext.Current;

        // Write thread-specific and global-fallback keys in parallel.
        // Always write usage:latest so the frontend can fall back to it when the
        // CopilotKit context threadId doesn't match the AG-UI threadId.
        var tasks = new List<Task>(2);
        if (tid is { Length: > 0 })
            tasks.Add(redis.SetAsync($"usage:{tid}:latest", json, _settings.ThreadTtl));
        tasks.Add(redis.SetAsync("usage:latest", json, UsageLatestTtl));
        await Task.WhenAll(tasks);
    }
}
