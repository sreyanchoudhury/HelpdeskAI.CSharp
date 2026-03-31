using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Agents;

/// <summary>
/// Captures CopilotKit frontend tools from the AG-UI request boundary and provides
/// them to workflow agents via <see cref="AIContextProvider"/>.
///
/// <para>
/// This works around the MAF limitation where <c>WorkflowHostAgent</c> passes
/// <c>AgentRunOptions: null</c> to child agents, which strips frontend render tools
/// such as <c>show_ticket_created</c> and <c>show_kb_article</c>.
/// </para>
/// </summary>
internal sealed class FrontendToolForwardingProvider(
    ILogger<FrontendToolForwardingProvider> logger) : AIContextProvider
{
    private static readonly AsyncLocal<IReadOnlyList<AITool>?> CapturedTools = new();
    private static readonly string[] FrontendToolPrefixes = ["show_", "suggest_"];

    public static void Capture(IReadOnlyList<AITool> tools) => CapturedTools.Value = tools;

    public static IReadOnlyList<AITool> CaptureFrontendTools(IEnumerable<AITool>? tools)
    {
        if (tools is null)
            return [];

        var frontendTools = tools
            .Where(IsFrontendTool)
            .ToList();

        if (frontendTools.Count > 0)
            Capture(frontendTools);

        return frontendTools;
    }

    public static void Clear() => CapturedTools.Value = null;

    public static bool IsFrontendTool(AITool tool) =>
        FrontendToolPrefixes.Any(prefix =>
            tool.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        AIContextProvider.InvokingContext context,
        CancellationToken ct)
    {
        var tools = CapturedTools.Value;
        logger.LogDebug("[FrontendTools] Providing {Count} captured CopilotKit tools",
            tools?.Count ?? 0);

        return ValueTask.FromResult(tools is { Count: > 0 }
            ? new AIContext { Tools = [.. tools] }
            : new AIContext());
    }
}
