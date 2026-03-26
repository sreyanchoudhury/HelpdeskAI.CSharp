using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Agents;

/// <summary>
/// Captures CopilotKit frontend tools from the AG-UI request boundary and provides
/// them to ALL agents in the workflow via <see cref="AIContextProvider"/>. Works around the MAF
/// limitation where <c>WorkflowHostAgent</c> passes <c>AgentRunOptions: null</c> to all agents,
/// stripping CopilotKit frontend tools (<c>show_ticket_created</c>, etc.).
///
/// <para>
/// Flow: AG-UI request arrives with frontend tools → <see cref="FrontendToolCapturingChatClient"/>
/// intercepts and calls <see cref="Capture"/> → each agent's context resolution calls
/// <see cref="ProvideAIContextAsync"/> which returns the captured tools → agents can call
/// render tools like <c>show_ticket_created</c>, <c>show_kb_article</c>, etc.
/// </para>
/// </summary>
internal sealed class FrontendToolForwardingProvider(
    ILogger<FrontendToolForwardingProvider> logger) : AIContextProvider
{
    // Request-scoped storage via AsyncLocal — set at AG-UI boundary, read by all workflow agents.
    private static readonly AsyncLocal<IReadOnlyList<AITool>?> _capturedTools = new();

    /// <summary>Capture CopilotKit frontend tools from the AG-UI request.</summary>
    public static void Capture(IReadOnlyList<AITool> tools) => _capturedTools.Value = tools;

    /// <summary>Clear captured tools after the request completes.</summary>
    public static void Clear() => _capturedTools.Value = null;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        AIContextProvider.InvokingContext context, CancellationToken ct)
    {
        var tools = _capturedTools.Value;
        logger.LogInformation("[FrontendTools] Providing {Count} captured CopilotKit tools",
            tools?.Count ?? 0);
        return ValueTask.FromResult(tools is { Count: > 0 }
            ? new AIContext { Tools = [.. tools] }
            : new AIContext());
    }
}
