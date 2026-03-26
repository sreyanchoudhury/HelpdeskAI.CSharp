using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Agents;

/// <summary>
/// Assembles the multi-agent handoff <see cref="Workflow"/> for the <c>/agent/v2</c> route.
///
/// <para>
/// Pattern: <c>AgentWorkflowBuilder.CreateHandoffBuilderWith(orchestrator)</c> — the
/// orchestrator classifies intent and hands off to the matching specialist. Each specialist
/// handles its domain end-to-end, then returns control to the orchestrator for the next turn.
/// The resulting <see cref="Workflow"/> is exposed via <c>workflow.AsAIAgent()</c> so it plugs
/// into the existing <c>app.MapAGUI("/agent/v2", ...)</c> without a custom HTTP endpoint.
/// </para>
///
/// <para>
/// History threading: individual specialist <see cref="ChatClientAgent"/> instances carry no
/// <c>ChatHistoryProvider</c>. The workflow runtime owns the message list and passes the full
/// conversation to each agent on every handoff — matching the documented MAF handoff pattern.
/// </para>
/// </summary>
internal static class HelpdeskWorkflowFactory
{
    /// <summary>
    /// Builds the handoff workflow. Each specialist receives only its domain-specific tool
    /// subset via a pre-filtered <see cref="DynamicToolSelectionProvider"/> instance.
    /// </summary>
    public static Workflow BuildWorkflow(
        IChatClient chatClient,
        AIContextProvider userProvider,
        AIContextProvider memoryProvider,
        AIContextProvider turnGuardProvider,
        AIContextProvider searchProvider,
        AIContextProvider attachmentProvider,
        AIContextProvider orchestratorAttachmentProvider,
        AIContextProvider frontendToolProvider,
        AIContextProvider ticketToolProvider,
        AIContextProvider kbToolProvider,
        AIContextProvider incidentToolProvider,
        ILoggerFactory? loggerFactory = null)
    {
        // orchestratorAttachmentProvider is peek-mode (reads without clearing) so the
        // orchestrator can see the attachment to route correctly, and diagnostic_agent
        // can still consume it via the clearing attachmentProvider below.
        // frontendToolProvider captures CopilotKit render tools (show_ticket_created, etc.)
        // and forwards them to the orchestrator, working around the MAF AgentRunOptions: null limitation.
        ChatClientAgent orchestratorAgent = OrchestratorAgentFactory.Create(
            chatClient, userProvider, memoryProvider, turnGuardProvider,
            orchestratorAttachmentProvider, frontendToolProvider, loggerFactory);

        // Each specialist receives frontendToolProvider so it can call render tools
        // (show_ticket_created, show_kb_article, etc.) directly after MCP tool calls.
        // The CopilotKit action tools write to the AG-UI SSE stream when invoked by
        // UseFunctionInvocation — this works because all agents share the same IChatClient pipeline.
        ChatClientAgent ticketAgent = TicketAgentFactory.Create(
            chatClient, userProvider, memoryProvider, turnGuardProvider, ticketToolProvider,
            frontendToolProvider, loggerFactory);

        ChatClientAgent kbAgent = KBAgentFactory.Create(
            chatClient, userProvider, memoryProvider, turnGuardProvider, searchProvider, kbToolProvider,
            frontendToolProvider, loggerFactory);

        ChatClientAgent incidentAgent = IncidentAgentFactory.Create(
            chatClient, userProvider, memoryProvider, turnGuardProvider, incidentToolProvider,
            frontendToolProvider, loggerFactory);

        ChatClientAgent diagnosticAgent = DiagnosticAgentFactory.Create(
            chatClient, userProvider, memoryProvider, turnGuardProvider, searchProvider, attachmentProvider,
            frontendToolProvider, loggerFactory);

        return AgentWorkflowBuilder
            .CreateHandoffBuilderWith(orchestratorAgent)
            .WithHandoffs(orchestratorAgent, [ticketAgent, kbAgent, incidentAgent, diagnosticAgent])
            .WithHandoffs([ticketAgent, kbAgent, incidentAgent, diagnosticAgent], orchestratorAgent)
            .Build();
    }
}
