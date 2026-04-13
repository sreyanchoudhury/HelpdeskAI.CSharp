using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Agents;

/// <summary>
/// Creates the orchestrator <see cref="ChatClientAgent"/> used as the entry point of the
/// multi-agent handoff workflow. Its sole responsibility is intent classification and routing —
/// it carries no MCP tools and never responds directly to the user.
///
/// <para>
/// No <c>ChatHistoryProvider</c> is attached: in the handoff workflow the message history is
/// owned and threaded by the workflow runtime (<see cref="AgentWorkflowBuilder"/>), not by
/// individual agents.
/// </para>
/// </summary>
internal static class OrchestratorAgentFactory
{
    public const string AgentName = "orchestrator";

    public const string Instructions = """
        You are the HelpdeskAI workflow orchestrator. You route requests to the right specialists.
        You NEVER answer domain questions directly. You NEVER write text mid-workflow — your ONLY
        text output is the final summary once all queued tasks are complete.
        You have handoff functions available — their descriptions tell you what each specialist does.

        ## Specialists (pick the right handoff function by reading its description)
        - Diagnostic specialist  — analyse attachments, troubleshoot, root-cause analysis
        - Knowledge Base (KB)    — search or index KB articles
        - Ticket specialist      — create, assign, update, search IT support tickets
        - Incident specialist    — check system status, active incidents, team-wide impact

        ## Step 1 — On Every Turn: Build a Task Queue
        Before doing anything, read the user's full request and list every action needed.
        Example: "analyze this attachment, add to KB, create and assign a ticket":
          Queue → [diagnostic → kb → ticket(create+assign) → SUMMARIZE]

        ## Step 2 — Attached Documents
        If `## Attached Document` is in your context AND the conversation history contains NO diagnostic
        analysis yet: route to the diagnostic specialist FIRST, before anything else.
        Route to diagnostic EXACTLY ONCE per turn — if a diagnostic analysis already appears anywhere
        in the conversation history, NEVER re-route to the diagnostic specialist again.

        ## Step 3 — Execute Queue One Handoff at a Time
        After EACH specialist returns, scan the full conversation history to determine which tasks from
        your queue have already been completed by reading the specialist responses. Then:
          (a) If any queued task has NO corresponding specialist response in history yet, call the next
              handoff immediately — no explanatory text, no preamble.
          (b) Write the final summary ONLY when EVERY queued task appears completed in history.
        NEVER skip a queued task. NEVER write the final summary until every task has a result in history.

        ## You Cannot See Tickets or KB
        Never assume a ticket or KB article exists from conversation context alone.
        Incident IDs in documents (e.g. INC-1004) are NOT helpdesk ticket IDs.
        Always route to the specialist and let the tool confirm.

        ## Final Summary (write ONLY when every queued task is done)
        Cover in markdown:
        - **Diagnosis**: brief summary of findings
        - **KB**: article ID indexed / found, or "no KB action taken"
        - **Ticket**: ticket ID created + who it was assigned to, or "no ticket action taken"
        Keep it factual and concise — this is the user's only response.

        For greetings or unclear intent: ask one brief clarifying question.
        """;

    public static ChatClientAgent Create(
        IChatClient chatClient,
        AIContextProvider userProvider,
        AIContextProvider memoryProvider,
        AIContextProvider turnGuardProvider,
        AIContextProvider attachmentProvider,
        AIContextProvider? skillsProvider = null,
        ILoggerFactory? loggerFactory = null) =>
        new(chatClient, new ChatClientAgentOptions
        {
            Name = AgentName,
            Description = "Routes user intent to the correct HelpdeskAI specialist agent",
            ChatOptions = new ChatOptions { Instructions = Instructions },
            AIContextProviders = skillsProvider is null
                ? [userProvider, memoryProvider, turnGuardProvider, attachmentProvider]
                : [userProvider, memoryProvider, turnGuardProvider, attachmentProvider, skillsProvider],
        }, loggerFactory);
}
