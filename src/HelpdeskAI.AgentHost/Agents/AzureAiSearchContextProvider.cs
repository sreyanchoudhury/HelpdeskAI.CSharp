using HelpdeskAI.AgentHost.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Agents;

/// <summary>
/// MAF <see cref="AIContextProvider"/> that injects Azure AI Search (RAG) results
/// into every agent turn before the LLM is invoked.
///
/// <para>
/// The base <see cref="AIContextProvider.InvokingCoreAsync"/> filters the input
/// to only external (caller-provided) messages before calling
/// <see cref="ProvideAIContextAsync"/>, so <c>context.AIContext.Messages</c> here
/// contains only the user's current turn � no history noise.
/// </para>
///
/// <para>
/// Nothing needs to be persisted after a turn, so
/// <see cref="AIContextProvider.StoreAIContextAsync"/> is intentionally left as the
/// no-op base implementation.
/// </para>
/// </summary>
internal sealed class AzureAiSearchContextProvider(
    IKnowledgeSearch knowledgeSearch,
    ILogger<AzureAiSearchContextProvider> log) : AIContextProvider
{
    public override string StateKey => nameof(AzureAiSearchContextProvider);

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

