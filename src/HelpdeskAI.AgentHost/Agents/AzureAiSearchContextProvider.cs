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
            ragContext = await knowledgeSearch.SearchAsync(userQuery, cancellationToken);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Azure AI Search failed � skipping RAG context");
        }

        if (string.IsNullOrWhiteSpace(ragContext))
            return new AIContext();

        return new AIContext
        {
            Messages = [new ChatMessage(ChatRole.System, ragContext)]
        };
    }
}

