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

        ## Rules
        - Tone: calm, factual, reassuring; include ETAs and workarounds when available.

        ## MANDATORY: Call the Handoff Function
        After completing all incident checks, you MUST call the handoff function (handoff_to_1).
        This is NOT optional — it is the only way the workflow continues.
        Call it even if you had nothing to do. Never skip it.
        """;

    public static ChatClientAgent Create(
        IChatClient chatClient,
        AIContextProvider userProvider,
        AIContextProvider memoryProvider,
        AIContextProvider turnGuardProvider,
        AIContextProvider toolSelectionProvider,
        ILoggerFactory? loggerFactory = null) =>
        new(chatClient, new ChatClientAgentOptions
        {
            Name = AgentName,
            Description = "Checks system status, active incidents, and team impact",
            ChatOptions = new ChatOptions { Instructions = Instructions },
            AIContextProviders = [userProvider, memoryProvider, turnGuardProvider, toolSelectionProvider],
        }, loggerFactory);
}
