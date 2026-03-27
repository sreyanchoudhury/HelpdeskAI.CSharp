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

    private static readonly string[] RetryKeywords =
    [
        "retry", "continue", "resume", "pick up", "finish the rest",
        "didn't work", "did not work", "still broken", "again"
    ];

    private static readonly string[] UrgencyKeywords =
    [
        "urgent", "asap", "immediately", "critical", "blocked", "sev1",
        "outage", "everyone", "whole team", "entire team", "all of us",
        "frustrated", "angry", "fed up"
    ];

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        AIContextProvider.InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var hasToolHistory = TurnStateContext.ToolCounts.Count > 0;
        var latestUserMessage = TurnStateContext.LastUserMessage;

        if (!hasToolHistory && string.IsNullOrWhiteSpace(latestUserMessage))
            return ValueTask.FromResult(new AIContext());

        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(latestUserMessage))
        {
            var normalized = latestUserMessage.Trim();
            var hasRetrySignal = RetryKeywords.Any(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            var hasUrgencySignal = UrgencyKeywords.Any(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            var hasBroadImpactSignal =
                normalized.Contains("team", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("everyone", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("multiple users", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("across", StringComparison.OrdinalIgnoreCase);

            if (hasRetrySignal || hasUrgencySignal || hasBroadImpactSignal)
            {
                lines.Add("## Current Turn Signals");

                if (hasRetrySignal)
                    lines.Add("The user appears to be retrying or resuming a partially completed workflow. Continue the remaining steps and reuse prior ticket or KB results where possible.");

                if (hasUrgencySignal)
                    lines.Add("The user's wording suggests urgency or frustration. Respond concisely and empathetically. If you create or update a ticket, choose priority and escalation actions that match the urgency described.");

                if (hasBroadImpactSignal)
                    lines.Add("The issue may affect multiple users or a whole team. Correlate with broader incident context when appropriate, but only call incident tools if the user is asking about outages, impact, or system-wide problems.");
            }
        }

        if (hasToolHistory)
        {
            lines.Add("## Current Turn Tool History");

            foreach (var kvp in TurnStateContext.ToolCounts.OrderBy(k => k.Key))
                lines.Add($"{kvp.Key}: {kvp.Value}");

            if (TurnStateContext.ToolCounts.Any(kvp => StatusTools.Contains(kvp.Key) && kvp.Value > 0))
                lines.Add("Status/incident tools already ran in this turn. Do not call them again unless the user explicitly asks for another live status check.");

            if (TurnStateContext.ToolCounts.TryGetValue("create_ticket", out var createdCount) && createdCount > 0)
                lines.Add("A ticket was already created in this turn. If the workflow is resuming or retrying, reuse that ticket and continue with the remaining steps instead of creating another one.");

            if (TurnStateContext.ToolCounts.TryGetValue("index_kb_article", out var indexedCount) && indexedCount > 0)
                lines.Add("A KB article was already indexed in this turn. If the workflow is resuming or retrying, reuse that article and continue with the remaining steps instead of indexing again.");
        }

        return ValueTask.FromResult(new AIContext
        {
            Messages = [new ChatMessage(ChatRole.System, string.Join('\n', lines))]
        });
    }
}
