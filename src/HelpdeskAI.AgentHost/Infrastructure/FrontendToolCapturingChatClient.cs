using HelpdeskAI.AgentHost.Agents;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Infrastructure;

/// <summary>
/// Intercepts the first chat call (from the AG-UI layer) which includes CopilotKit
/// frontend tools in <see cref="ChatOptions.Tools"/>. Captures them in <see cref="AsyncLocal{T}"/>
/// via <see cref="FrontendToolForwardingProvider.Capture"/> so the orchestrator agent can
/// call render tools (<c>show_ticket_created</c>, <c>show_kb_article</c>, etc.) even though
/// MAF's <c>WorkflowHostAgent</c> passes <c>AgentRunOptions: null</c>.
///
/// <para>
/// Only tools whose name starts with <c>show_</c> or <c>suggest_</c> are captured — these
/// are the CopilotKit render actions. MCP backend tools are provided separately via
/// <see cref="DynamicToolSelectionProvider"/> and are not affected.
/// </para>
/// </summary>
internal sealed class FrontendToolCapturingChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        CaptureIfPresent(options);
        return await base.GetResponseAsync(messages, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        CaptureIfPresent(options);
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            yield return update;
    }

    private static void CaptureIfPresent(ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 } tools)
            return;

        var frontendTools = tools.Where(IsFrontendTool).ToList();
        if (frontendTools.Count > 0)
            FrontendToolForwardingProvider.Capture(frontendTools);
    }

    private static bool IsFrontendTool(AITool tool) =>
        tool.Name.StartsWith("show_", StringComparison.OrdinalIgnoreCase) ||
        tool.Name.StartsWith("suggest_", StringComparison.OrdinalIgnoreCase);
}
