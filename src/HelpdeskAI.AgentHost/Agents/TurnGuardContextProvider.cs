using HelpdeskAI.AgentHost.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Agents;

internal sealed class TurnGuardContextProvider : AIContextProvider
{
    private static readonly HashSet<string> StatusTools =
    [
        "get_system_status",
        "get_active_incidents",
        "check_impact_for_team"
    ];

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        AIContextProvider.InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        if (TurnStateContext.ToolCounts.Count == 0)
            return ValueTask.FromResult(new AIContext());

        var lines = new List<string> { "## Current Turn Tool History" };

        foreach (var kvp in TurnStateContext.ToolCounts.OrderBy(k => k.Key))
            lines.Add($"{kvp.Key}: {kvp.Value}");

        if (TurnStateContext.ToolCounts.Any(kvp => StatusTools.Contains(kvp.Key) && kvp.Value > 0))
            lines.Add("Status/incident tools already ran in this turn. Do not call them again unless the user explicitly asks for another live status check.");

        if (TurnStateContext.ToolCounts.TryGetValue("create_ticket", out var createdCount) && createdCount > 0)
            lines.Add("A ticket was already created in this turn. Do not create another ticket unless the user explicitly asks for a second one.");

        return ValueTask.FromResult(new AIContext
        {
            Messages = [new ChatMessage(ChatRole.System, string.Join('\n', lines))]
        });
    }
}
