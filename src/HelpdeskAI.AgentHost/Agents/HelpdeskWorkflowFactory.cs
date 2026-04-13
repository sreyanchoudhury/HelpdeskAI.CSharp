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
    private const string TelemetrySource = "HelpdeskAI.AgentHost";

    public static Workflow BuildWorkflow(
        IChatClient chatClient,
        AIContextProvider userProvider,
        AIContextProvider memoryProvider,
        AIContextProvider turnGuardProvider,
        AIContextProvider searchProvider,
        AIContextProvider attachmentProvider,
        AIContextProvider orchestratorAttachmentProvider,
        AIContextProvider ticketToolProvider,
        AIContextProvider kbToolProvider,
        AIContextProvider incidentToolProvider,
        AIContextProvider? skillsProvider = null,
        bool enableSensitiveData = false,
        ILoggerFactory? loggerFactory = null)
    {
        // Wrap each agent with OpenTelemetryAgent so the Agents (Preview) view in App Insights
        // shows per-specialist spans (invoke_agent orchestrator, invoke_agent ticket_agent, etc.)
        // with proper gen_ai.agent.name attribution and model info as child LLM spans.
        AIAgent orchestratorAgent = new OpenTelemetryAgent(
            OrchestratorAgentFactory.Create(chatClient, userProvider, memoryProvider, turnGuardProvider,
                orchestratorAttachmentProvider, skillsProvider, loggerFactory),
            TelemetrySource) { EnableSensitiveData = enableSensitiveData };

        AIAgent ticketAgent = new OpenTelemetryAgent(
            TicketAgentFactory.Create(chatClient, userProvider, memoryProvider, turnGuardProvider,
                ticketToolProvider, loggerFactory),
            TelemetrySource) { EnableSensitiveData = enableSensitiveData };

        AIAgent kbAgent = new OpenTelemetryAgent(
            KBAgentFactory.Create(chatClient, userProvider, memoryProvider, turnGuardProvider,
                searchProvider, kbToolProvider, loggerFactory),
            TelemetrySource) { EnableSensitiveData = enableSensitiveData };

        AIAgent incidentAgent = new OpenTelemetryAgent(
            IncidentAgentFactory.Create(chatClient, userProvider, memoryProvider, turnGuardProvider,
                incidentToolProvider, loggerFactory),
            TelemetrySource) { EnableSensitiveData = enableSensitiveData };

        AIAgent diagnosticAgent = new OpenTelemetryAgent(
            DiagnosticAgentFactory.Create(chatClient, userProvider, memoryProvider, turnGuardProvider,
                searchProvider, attachmentProvider, skillsProvider, loggerFactory),
            TelemetrySource) { EnableSensitiveData = enableSensitiveData };

        // Override MAF's default "if appropriate" handoff instructions with mandatory language.
        // The default says agents CAN hand off — we need them to ALWAYS hand off when done.
        const string mandatoryHandoffInstructions =
            "You are one specialist agent in a multi-agent helpdesk system. " +
            "Handoffs are achieved by calling a handoff function (named handoff_to_1, handoff_to_2, etc.). " +
            "MANDATORY: You MUST call a handoff function when you have finished your work. " +
            "Calling the handoff function is the ONLY way to signal completion — do not skip it. " +
            "Never narrate or mention the handoff in your text response.";

        return AgentWorkflowBuilder
            .CreateHandoffBuilderWith(orchestratorAgent)
            .WithHandoffInstructions(mandatoryHandoffInstructions)
            // HandoffOnly: strips handoff_to_N calls from history but keeps domain tool results
            // (create_ticket, index_kb_article etc.) so specialists see prior outputs for dedup.
            .WithHandoffs(orchestratorAgent, [ticketAgent, kbAgent, incidentAgent, diagnosticAgent])
            .WithHandoffs([ticketAgent, kbAgent, incidentAgent, diagnosticAgent], orchestratorAgent)
            .Build();
    }
}
