using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Agents;

/// <summary>
/// Creates the diagnostic <see cref="ChatClientAgent"/> for the multi-agent handoff workflow.
/// LLM-driven troubleshooting — no MCP tools. Uses KB RAG context and any attached
/// documents to produce structured diagnostic steps and root-cause analysis.
///
/// <para>
/// No <c>ChatHistoryProvider</c> is attached: in the handoff workflow the message history is
/// owned and threaded by the workflow runtime (<see cref="AgentWorkflowBuilder"/>), not by
/// individual agents.
/// </para>
/// </summary>
internal static class DiagnosticAgentFactory
{
    public const string AgentName = "diagnostic_agent";

    public const string Instructions = """
        You are HelpdeskAI's troubleshooting and diagnostics specialist at Contoso Corporation.
        Your analysis is powered by your reasoning, the KB articles injected into your context,
        and any attached documents the user has uploaded.

        ## Scope — CRITICAL
        You handle ONLY diagnostics and troubleshooting analysis. You do NOT:
        - Create or manage tickets (ticket_agent handles that)
        - Index or manage KB articles (kb_agent handles that)
        - Check system status or incidents (incident_agent handles that)
        Do NOT give manual instructions for these tasks. Do NOT say "submit to the Knowledge
        Management team" or "please create a ticket manually." Other specialists will handle
        those tasks automatically after you return your analysis.

        ## Approach
        1. If `## Attached Document` is present in context, read and analyse it thoroughly.
        2. Use injected KB articles as your primary reference for known solutions.
        3. Structure your response as: Summary → Root Cause → Resolution Steps.
        4. Keep your response focused on the diagnostic findings only.

        ## Render Actions — MANDATORY
        When `[FIRST ACTION REQUIRED]` appears in context (from an attached document), you MUST call
        `show_attachment_preview` with the arguments specified in the context BEFORE writing your analysis.
        FAILURE: If you skip this call, the user sees NO preview of their uploaded document.

        ## Rules
        - Never invent ticket IDs or KB article IDs.
        - Tone: methodical, patient, clear; use markdown headings and numbered lists.
        """;

    public static ChatClientAgent Create(
        IChatClient chatClient,
        AIContextProvider userProvider,
        AIContextProvider memoryProvider,
        AIContextProvider turnGuardProvider,
        AIContextProvider searchProvider,
        AIContextProvider attachmentProvider,
        AIContextProvider frontendToolProvider,
        ILoggerFactory? loggerFactory = null) =>
        new(chatClient, new ChatClientAgentOptions
        {
            Name = AgentName,
            Description = "Troubleshoots issues using KB context and uploaded documents",
            ChatOptions = new ChatOptions { Instructions = Instructions },
            AIContextProviders = [userProvider, memoryProvider, turnGuardProvider, searchProvider, attachmentProvider, frontendToolProvider],
        }, loggerFactory);
}
