using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Agents;

/// <summary>
/// Creates the incident-domain <see cref="ChatClientAgent"/> for the multi-agent handoff workflow.
/// Handles system status checks, active incident queries, and team impact assessments.
///
/// <para>
/// This agent is structurally isolated — it has no ticket or KB tools, so proactive
/// incident calls are impossible regardless of LLM behaviour.
/// </para>
/// <para>
/// No <c>ChatHistoryProvider</c> is attached: in the handoff workflow the message history is
/// owned and threaded by the workflow runtime (<see cref="AgentWorkflowBuilder"/>), not by
/// individual agents.
/// </para>
/// </summary>
internal static class IncidentAgentFactory
{
    public const string AgentName = "incident_agent";

    public static readonly string[] AllowedTools =
    [
        "get_system_status", "get_active_incidents", "check_impact_for_team",
    ];

    public const string Instructions = """
        You are HelpdeskAI's incident and system-health specialist at Contoso Corporation.

        ## Tools
        - `get_system_status`      — check the overall IT service health dashboard
        - `get_active_incidents`   — list all currently open incidents
        - `check_impact_for_team`  — assess which active incidents affect a specific team

        ## Tool Rules
        - Call tools one at a time, sequentially.
        - Call status/incident tools ONLY when the user explicitly asks — never proactively.
        - If the user asks "what issues affect my team?" and no team is known from context, ask which team.
        - Never call the same status tool twice in one turn unless the user explicitly requests a second check.

        ## Render Actions — MANDATORY (never skip)
        After EVERY successful tool call that returns incident data, you MUST call:
        - `get_system_status` (incidents found)    → call `show_incident_alert` with the result's `_renderArgs`
        - `get_active_incidents` (incidents found)  → call `show_incident_alert` with the result's `_renderArgs`
        - `check_impact_for_team` (incidents found) → call `show_incident_alert` with the result's `_renderArgs`
        Sequence: 1) call MCP tool  2) call render tool  3) write text summary.
        FAILURE: If you skip the render tool, the user sees NO visual card.

        ## Rules
        - Tone: calm, factual, reassuring; include ETAs and workarounds when available.
        """;

    public static ChatClientAgent Create(
        IChatClient chatClient,
        AIContextProvider userProvider,
        AIContextProvider memoryProvider,
        AIContextProvider turnGuardProvider,
        AIContextProvider toolSelectionProvider,
        AIContextProvider frontendToolProvider,
        ILoggerFactory? loggerFactory = null) =>
        new(chatClient, new ChatClientAgentOptions
        {
            Name = AgentName,
            Description = "Checks system status, active incidents, and team impact",
            ChatOptions = new ChatOptions { Instructions = Instructions },
            AIContextProviders = [userProvider, memoryProvider, turnGuardProvider, toolSelectionProvider, frontendToolProvider],
        }, loggerFactory);
}
