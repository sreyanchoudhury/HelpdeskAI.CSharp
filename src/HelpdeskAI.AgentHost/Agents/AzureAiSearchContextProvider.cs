using HelpdeskAI.AgentHost.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Agents;

internal sealed class AzureAiSearchContextProvider(
    IKnowledgeSearch knowledgeSearch,
    ILogger<AzureAiSearchContextProvider> log) : AIContextProvider
{
    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        AIContextProvider.InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var userQuery = context.AIContext.Messages
            ?.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(userQuery))
            return new AIContext();

        string? ragContext = null;
        try
        {
            // Use an independent timeout — not the raw request CT — so a dropped SSE connection
            // from the previous turn does not cancel RAG context gathering for the next turn.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            ragContext = await knowledgeSearch.SearchAsync(userQuery, cts.Token);
        }
        catch (OperationCanceledException)
        {
            log.LogDebug("Azure AI Search timed out — skipping RAG context");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Azure AI Search failed — skipping RAG context");
        }

        if (string.IsNullOrWhiteSpace(ragContext))
            return new AIContext();

        return new AIContext
        {
            Messages = [new ChatMessage(ChatRole.System, ragContext)]
        };
    }
}
