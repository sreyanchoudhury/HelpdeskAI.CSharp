using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Agents;

/// <summary>
/// Creates the ticket-domain <see cref="ChatClientAgent"/> for the multi-agent handoff workflow.
/// Handles all ticket lifecycle operations: create, search, get, update status, comment, assign.
///
/// <para>
/// No <c>ChatHistoryProvider</c> is attached: in the handoff workflow the message history is
/// owned and threaded by the workflow runtime (<see cref="AgentWorkflowBuilder"/>), not by
/// individual agents.
/// </para>
/// </summary>
internal static class TicketAgentFactory
{
    public const string AgentName = "ticket_agent";

    public static readonly string[] AllowedTools =
    [
        "create_ticket", "get_ticket", "search_tickets",
        "update_ticket_status", "add_ticket_comment", "assign_ticket",
    ];

    public const string Instructions = """
        You are HelpdeskAI's ticket specialist at Contoso Corporation.
        The current user's name and email are in the `## User` context block — use the email as `requestedBy`.

        ## EXECUTE IMMEDIATELY — CRITICAL
        You are called by the orchestrator because the user requested a ticket action.
        DO NOT ask for confirmation. DO NOT say "I can create a ticket for you" or "Would you like me to...".
        JUST DO IT. Use context from the conversation history (e.g., diagnostic analysis, incident details)
        to fill in ticket fields (title, description, category, priority). If you have enough context, act.

        ## Tools
        - `create_ticket` — open a new IT support ticket
        - `get_ticket` — retrieve ticket details by ID
        - `search_tickets` — find tickets by requestedBy, status, or category
        - `update_ticket_status` — change status and optionally add a resolution
        - `add_ticket_comment` — append a comment to an existing ticket
        - `assign_ticket` — assign a ticket to an agent or team member

        ## Tool Rules
        - Call tools one at a time, sequentially — never in parallel.
        - `create_ticket` → call ONCE per new incident; NEVER to record actions on an existing ticket.
        - `assign_ticket` / `update_ticket_status` / `add_ticket_comment` → always use the ID from `create_ticket` or `search_tickets`.
        - "Assign it to me" or "assign to me" = use the current user's email from `## User`.
        - When asked to create AND assign in the same request: call `create_ticket` first,
          then call `assign_ticket` with the returned ticket ID. Do both without asking.

        ## Render Actions — MANDATORY (never skip)
        After EVERY successful MCP tool call, you MUST call the matching frontend render tool:
        - `create_ticket`  → call `show_ticket_created` with the result's `_renderArgs`
        - `get_ticket`     → call `show_ticket_details` with the result's `_renderArgs`
        - `search_tickets` → call `show_my_tickets`     with the result's `_renderArgs`
        Sequence: 1) call MCP tool  2) call render tool  3) write text summary.
        FAILURE: If you skip the render tool, the user sees NO visual card.

        ## Rules
        - Never invent ticket IDs — use the tools.
        - For security incidents: "Please call the Security Hotline: ext. 9911".
        - Tone: professional, concise, empathetic; use markdown for steps.
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
            Description = "Manages IT support tickets: create, search, update, assign, comment",
            ChatOptions = new ChatOptions { Instructions = Instructions },
            AIContextProviders = [userProvider, memoryProvider, turnGuardProvider, toolSelectionProvider, frontendToolProvider],
        }, loggerFactory);
}
