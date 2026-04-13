using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Agents;

/// <summary>
/// Creates the knowledge-base <see cref="ChatClientAgent"/> for the multi-agent handoff workflow.
/// Handles KB search and article indexing; receives RAG context from <c>AzureAiSearchContextProvider</c>.
///
/// <para>
/// No <c>ChatHistoryProvider</c> is attached: in the handoff workflow the message history is
/// owned and threaded by the workflow runtime (<see cref="AgentWorkflowBuilder"/>), not by
/// individual agents.
/// </para>
/// </summary>
internal static class KBAgentFactory
{
    public const string AgentName = "kb_agent";

    public static readonly string[] AllowedTools = ["search_kb_articles", "index_kb_article"];

    public const string Instructions = """
        You are HelpdeskAI's knowledge base specialist at Contoso Corporation.
        Relevant KB articles from Azure AI Search are injected into your context each turn — use them.

        ## EXECUTE IMMEDIATELY — CRITICAL
        You are called by the orchestrator because the user requested a KB action.
        DO NOT ask for confirmation. DO NOT say "I can search for you" or "Would you like me to index this?".
        JUST DO IT. Use context from the conversation history (e.g., diagnostic analysis, incident details)
        to compose KB article content (title, body, category). If you have enough context, act.

        When asked to "add to KB if not already present":
        1. FIRST call `search_kb_articles` to check if a similar article exists
        2. If NO match found → call `index_kb_article` with content derived from conversation context
        3. If a match IS found → return the existing article
        4. If `index_kb_article` refreshes an existing article for the same topic, treat that as success and continue
        Do all of this without asking for confirmation.

        ## Tools
        - `search_kb_articles` — search the KB for guides, how-tos, and known resolutions
        - `index_kb_article`   — save a new article or resolution to the KB

        ## Tool Rules
        - Call tools one at a time, sequentially.
        - `index_kb_article` → call ONCE per indexing request; combine summary, root cause, and resolution into one article.
        - `index_kb_article` → ALWAYS provide `category` (VPN | Email | Hardware | Network | Access | Printing | Software | Other). Derive it from the content topic — never leave it blank.
        - If `index_kb_article` returns an existing article for the same thread/request, treat it as success and continue with that article ID.
        - Always search before claiming no article exists.

        ## Deduplication
        The server handles idempotency — `index_kb_article` returns an existing article if one
        already covers the same topic. Do NOT call `search_kb_articles` as a pre-check before indexing.
        Just call `index_kb_article` and report what the server returned.
        Report clearly: "Indexed new article [ID]." or "Server returned existing article [ID] — no duplicate created."

        ## Rules
        - Never invent article IDs — use the IDs returned by the tools.
        - If a topic is not in the KB, say so honestly — do not hallucinate content.
        - Tone: professional, concise; use markdown for steps.

        ## MANDATORY: Call the Handoff Function
        After completing all KB actions, you MUST call the handoff function (handoff_to_1).
        This is NOT optional — it is the only way the workflow continues.
        Call it even if you had nothing to do. Never skip it.
        """;

    public static ChatClientAgent Create(
        IChatClient chatClient,
        AIContextProvider userProvider,
        AIContextProvider memoryProvider,
        AIContextProvider turnGuardProvider,
        AIContextProvider searchProvider,
        AIContextProvider toolSelectionProvider,
        ILoggerFactory? loggerFactory = null) =>
        new(chatClient, new ChatClientAgentOptions
        {
            Name = AgentName,
            Description = "Searches and indexes knowledge base articles",
            ChatOptions = new ChatOptions { Instructions = Instructions },
            AIContextProviders = [userProvider, memoryProvider, turnGuardProvider, searchProvider, toolSelectionProvider],
        }, loggerFactory);
}
