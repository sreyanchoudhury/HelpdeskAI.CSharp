using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HelpdeskAI.AgentHost.Infrastructure;

/// <summary>
/// Intercepts chat client calls to capture token usage from the LLM response
/// and write it directly to Redis while the streaming response is still open.
/// Writing inside the generator (before the HTTP response closes) avoids a race
/// condition where the frontend fetches /agent/usage before StoreChatHistoryAsync runs.
/// Also emits structured token-count traces picked up by Azure Monitor as
/// customDimensions (PromptTokens, CompletionTokens) for Phase 1d KQL baseline queries.
/// </summary>
internal sealed class UsageCapturingChatClient(
    IChatClient inner,
    IRedisService redis,
    IOptions<ConversationSettings> settings,
    ILogger<UsageCapturingChatClient> logger) : DelegatingChatClient(inner)
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

        var aggregated = updates.ToChatResponse();
        if (aggregated.Usage is { } u)
            await PersistUsageAsync(u.InputTokenCount ?? 0, u.OutputTokenCount ?? 0);
    }

    private async Task PersistUsageAsync(long promptTokens, long completionTokens)
    {
        var tid = ThreadIdContext.Current;

        logger.LogInformation(
            "Token usage - PromptTokens: {PromptTokens}, CompletionTokens: {CompletionTokens}, ThreadId: {ThreadId}.",
            promptTokens, completionTokens, tid ?? "(none)");

        if (tid is not { Length: > 0 })
            return;

        var json = JsonSerializer.Serialize(new { promptTokens, completionTokens });
        await redis.SetAsync($"usage:{tid}:latest", json, _settings.ThreadTtl);
    }
}
