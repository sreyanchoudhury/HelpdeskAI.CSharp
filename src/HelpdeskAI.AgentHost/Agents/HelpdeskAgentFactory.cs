using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Agents;

/// <summary>
/// Creates the <see cref="AIAgent"/> passed to <c>app.MapAGUI()</c>.
///
/// <para>
/// Per-turn concerns are now handled by MAF providers registered in
/// <see cref="ChatClientAgentOptions"/> rather than a custom
/// <c>DelegatingChatClient</c>:
/// <list type="bullet">
///   <item><see cref="RedisChatHistoryProvider"/> via <c>ChatHistoryProvider</c> -
///     loads, reduces and persists conversation history in Redis.</item>
///   <item><see cref="AzureAiSearchContextProvider"/> via <c>AIContextProviders</c> -
///     injects RAG context from Azure AI Search before each LLM call.</item>
/// </list>
/// </para>
/// </summary>
public static class HelpdeskAgentFactory
{
	public const string AgentName = "HelpdeskAgent";

	public const string BaseInstructions = """
        You are **HelpdeskAI**, a senior IT support specialist at Contoso Corporation.

        ## Capabilities
        - Answer IT questions using the knowledge base articles shown above in context
        - Create, update and search support tickets using your tools
        - Guide users through step-by-step troubleshooting

        ## Workflow
        1. Read the knowledge base context (injected above) before answering
        2. For ongoing issues, check existing tickets first with search_tickets
        3. Provide numbered troubleshooting steps from KB articles when available
        4. Create a ticket if the issue needs tracking or human intervention
        5. Always confirm ticket IDs and KB article IDs back to the user

        ## Rules
        - Never invent ticket IDs or KB article IDs - use the tools
        - Ask for the user's email before creating tickets if not provided
        - For security incidents: "Please call the Security Hotline: ext. 9911"

        ## Tone
        Professional, concise, empathetic. Use markdown formatting for steps.
        """;

	public static AIAgent Create(
		IChatClient chatClient,
		IReadOnlyList<AIFunction> tools,
		ChatHistoryProvider historyProvider,
		AIContextProvider searchProvider) =>
		chatClient.AsAIAgent(new ChatClientAgentOptions
		{
			Name = AgentName,
			ChatOptions = new ChatOptions
			{
				Instructions = BaseInstructions,
				Tools = [.. tools.Cast<AITool>()]
			},
			ChatHistoryProvider = historyProvider,
			AIContextProviders = [searchProvider]
		});
}
