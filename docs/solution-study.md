# HelpdeskAI — Full Technical Recap

> A component-by-component deep dive covering every concept, pattern, and design decision in the solution. Written for engineers who need to answer questions about any part of the system.

---

## 1. Architecture Overview

Three services, each a separate Azure Container App:

```
Browser (Next.js 16 + React 19 + CopilotKit 1.54.1)
        │  AG-UI protocol over HTTP/SSE
        ▼
AgentHost  (.NET 10, ASP.NET Core)
  ├── /agent        → v1: single ChatClientAgent  (MAF)
  └── /agent/v2     → v2: MAF HandoffWorkflow — Orchestrator + 4 specialists
        │
        │  MCP Streamable HTTP
        ▼
McpServer  (.NET 10, ASP.NET Core Minimal API)
  ├── TicketTools     → Azure Cosmos DB
  ├── KBTools         → Azure AI Search
  ├── IncidentTools   → in-memory SystemStatusService
  └── (Attachment upload/processing exposed via AgentHost endpoints)

Redis  (Container App, redis:7-alpine, no persistence)
Azure Cosmos DB, Azure AI Search, Azure Blob Storage, Azure Document Intelligence
  — all reached from AgentHost or McpServer directly
```

Only the AgentHost is internet-facing. McpServer is internal. The Frontend proxies AG-UI requests through a Next.js API route (`/api/copilotkit`) to avoid exposing the AgentHost URL to the browser.

### Package layers (AgentHost)

| Layer | Package | Version |
|---|---|---|
| AI abstractions | `Microsoft.Extensions.AI` | 10.4.1 |
| MAF core | `Microsoft.Agents.AI` | 1.0.0 |
| MAF AG-UI host | `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` | 1.0.0-preview |
| MAF workflows | `Microsoft.Agents.AI.Workflows` | 1.0.0 |
| Azure OpenAI client | `Azure.AI.OpenAI` | 2.8.0-beta.1 |
| MCP client | `ModelContextProtocol` | 1.2.0 |
| Redis | `StackExchange.Redis` | 2.12.14 |

McpServer uses `ModelContextProtocol.AspNetCore` 1.2.0 and `Microsoft.Azure.Cosmos` 3.58.0.

---

## 2. AG-UI Protocol

AG-UI is an open wire protocol for streaming agent responses to a frontend. It runs on top of HTTP Server-Sent Events (SSE). Instead of returning one JSON blob when complete, the agent opens an SSE stream and pushes typed events in real time:

- `RUN_STARTED` — agent accepted the request
- `TEXT_MESSAGE_CONTENT` — one token chunk of assistant text
- `TOOL_CALL_START` / `TOOL_CALL_END` — agent calling a tool (visible in the UI)
- `STATE_SNAPSHOT` / `STATE_DELTA` — CopilotKit shared state sync
- `RUN_FINISHED` — all done
- `RUN_ERROR { message }` — failure (e.g. content filter hit); CopilotKit renders this via `onError`

**Why SSE not WebSockets?** Unidirectional, works through all standard HTTP proxies and load balancers, no handshake, Azure Container Apps handles it natively. Simpler operational story.

**`app.MapAGUI("/agent", agent)`** — single MAF extension method that registers the SSE POST endpoint, deserialises the `RunAgentInput` (which contains `threadId`, `messages`, `state`), wires CopilotKit shared state, and calls `agent.RunStreamingAsync(...)`. It also registers `UseAgentRequestContext()` middleware.

**`UseAgentRequestContext()`** — MAF ASP.NET Core middleware. Runs on every request before any handler. Deserialises the AG-UI `threadId` from the request body and writes it into `ThreadIdContext` (via `AsyncLocal`). Also extracts the JWT claims (`name`, `preferred_username`/`email`) and writes them into `UserContext` (also `AsyncLocal`). This is the root of session identity for everything downstream.

---

## 3. AsyncLocal — What It Is, Why It Matters

```csharp
internal static class ThreadIdContext
{
    private static readonly AsyncLocal<string?> _current = new();
    public static string? Current => _current.Value;
    internal static void Set(string? threadId) => _current.Value = threadId;
}
```

**What `AsyncLocal<T>` is:** A value stored in the current execution context that flows *downward* through the async call chain automatically. Any `await` continuation — even if it resumes on a different thread pool thread — inherits the value from its parent. Setting it in middleware means every async method called during that request can read it without being passed it as a parameter. This is the same mechanism `IHttpContextAccessor` uses.

**Why it is used here:** Every Redis key in the system is namespaced by `threadId`. Without `AsyncLocal`, every class — every provider, every middleware layer, every tool wrapper — would need an explicit `threadId` parameter threaded through. `AsyncLocal` makes it ambient: set it once at the request boundary, read it anywhere in the call chain.

**`TurnStateContext`** uses the same pattern with two `AsyncLocal` fields:
- `_lastUserMessage` — the raw user turn text, for signal detection in `TurnGuardContextProvider`
- `_toolCounts` — a `ConcurrentDictionary<string, int>` tracking how many times each tool was called this turn; used for in-turn dedup

**The v2 problem:** MAF's `WorkflowHostAgent` spawns tasks per specialist using `ExecutionContext.SuppressFlow()` — a deliberate performance decision that cuts `AsyncLocal` inheritance so parent context doesn't leak into child tasks. `ThreadIdContext.Current` and `FrontendToolForwardingProvider`'s captured tools both become `null` on the other side of that boundary.

**The fix:** `ThreadIdPreservingChatClient` — a `DelegatingChatClient` in the v2 pipeline. On the first call (which still has the value from the AG-UI boundary), it captures `ThreadIdContext.Current` and the frontend tools into instance fields. On every subsequent call, if `ThreadIdContext.Current` is null, it restores from the captured value and logs a warning. This is why it exists only in the v2 pipeline.

---

## 4. The IChatClient Pipeline

`IChatClient` is MEAI's abstraction for a language model call. Two methods: `GetResponseAsync` and `GetStreamingResponseAsync`. `DelegatingChatClient` is the base class for wrapping it — exactly like `DelegatingHandler` in `HttpClient`. Each layer calls `base.Get...Async(...)` and can intercept before, after, or wrap the call.

**v1 pipeline (registered via `AddChatClient`):**

```
AzureOpenAI raw client  (gpt-5.3-chat)
    └── FunctionInvokingChatClient  (.UseFunctionInvocation())
        └── ContentSafetyGuardChatClient
            └── UsageCapturingChatClient
                └── AGUIHistoryNormalizingClient
                    └── LoggingChatClient  (.UseLogging())
                        └── OpenTelemetryChatClient  (.UseOpenTelemetry())
```

**v2 pipeline (registered as keyed singleton `"v2-chat"` via `AddKeyedSingleton`):**

```
AzureOpenAI raw client  (gpt-5.2-chat)
    └── ContentSafetyGuardChatClient
        └── ThreadIdPreservingChatClient
            └── UsageCapturingChatClient
                └── AGUIHistoryNormalizingClient
                    └── LoggingChatClient
                        └── OpenTelemetryChatClient
```

**The critical v1 vs v2 difference — `UseFunctionInvocation()`:**

In v1, `FunctionInvokingChatClient` intercepts `FunctionCallContent` from the model, looks up the matching `AIFunction` in `ChatOptions.Tools`, invokes it, and feeds the result back as `ToolResultContent` before the next model call. This is how regular MCP tools are executed.

In v2, this is intentionally omitted. MAF's `ChatClientAgent` detects whether a `FunctionInvokingChatClient` is already present in the pipeline. If not, it inserts its own handoff-aware version. The handoff tools (`handoff_to_1`, `handoff_to_2`, etc.) are `AIFunctionDeclaration` objects — declarations with no implementation. The standard `FunctionInvokingChatClient` only executes `AIFunction` instances; it leaves `AIFunctionDeclaration` as unresolved `FunctionCallContent` in the stream. MAF's internal executor watches for those and routes to the target agent. If you add `UseFunctionInvocation()` externally, it runs *inside* the pipeline and processes everything before MAF's executor sees it — handoff calls disappear and the model narrates the handoff as text instead of executing it.

---

## 5. ContentSafetyGuardChatClient

Azure OpenAI returns HTTP 400 with `error.code = "content_filter"` when a request is blocked by its built-in content filters. Without handling this, the SSE stream abruptly terminates — the frontend gets a broken pipe with no user message and no recovery path.

The guard catches this in both streaming and non-streaming paths:
1. Deletes the poisoned Redis keys: `messages:{threadId}`, `attachments:{threadId}`, `usage:{threadId}:latest`, and all `sideeffect:{threadId}:*` keys (via prefix scan). Without this, the bad message stays in history and triggers the filter again on every subsequent turn.
2. Logs a structured warning with `threadId` for App Insights correlation.
3. Throws `ContentSafetyException(UserFacingMessage, innerException)`.

MAF catches the unhandled exception from the streaming iterator and emits `RUN_ERROR { message: "⚠️ Your request was blocked..." }`. CopilotKit's `onError` callback resets local state and displays the message as an assistant bubble.

**v2 additional complexity:** `WorkflowHostAgent` catches exceptions from child agents and wraps them in `ErrorContent` rather than re-throwing — it doesn't want one specialist failure to kill the whole workflow host. `WorkflowAgentWrapperFactory` inspects every `AgentResponse` and `AgentResponseUpdate` for `ErrorContent` containing "blocked by Azure content safety" or "content_filter", converts it back into a thrown `ContentSafetyException`, and lets the outer SSE stream carry it to CopilotKit. Without this wrapper, content safety failures in v2 would silently produce an empty response.

---

## 6. AGUIHistoryNormalizingClient

OpenAI's API requires that when the model makes multiple parallel tool calls, they appear as a *single* assistant message containing all `FunctionCallContent` items, followed by individual `ToolResultContent` messages. CopilotKit's AG-UI history sometimes reconstructs parallel tool calls as separate consecutive assistant messages (one per tool call).

This client normalises the message list on every call: it scans for consecutive assistant messages that contain only `FunctionCallContent` and no text, merges them into a single assistant message with all the calls combined, preserving the subsequent tool results. Without it, Azure OpenAI returns a 400 validation error about malformed tool call history.

---

## 7. UsageCapturingChatClient

Captures `InputTokenCount` and `OutputTokenCount` from every LLM response and writes them to `usage:{threadId}:latest` in Redis. The frontend polls `GET /agent/usage?threadId=...` after `RUN_FINISHED` to show the turn's token usage.

**Streaming race condition:** Azure OpenAI sends token usage only in the final SSE chunk. If the client writes to Redis after the async iterator completes but the HTTP response is still flushing, the frontend might poll before the write finishes. The fix: write to Redis *inside* the iterator — after the last `yield return` but before the iterator exits — so the write is guaranteed to land before the HTTP response closes.

**`IncludeStreamingUsagePolicy`:** Azure OpenAI's streaming API doesn't include usage data by default. This custom `PipelinePolicy` (from Azure SDK's `System.ClientModel.Primitives`) adds `stream_options: { include_usage: true }` to every streaming request via a `PerCall` pipeline hook on the `AzureOpenAIClientOptions`.

---

## 8. AIContextProviders — The Per-Turn Context System

`AIContextProvider` is a MAF abstraction. Before calling the model, MAF calls each registered provider's `ProvideAIContextAsync(InvokingContext)`. Each returns an `AIContext` that can carry extra `ChatMessage` objects (injected as system messages), additional `AITool` objects (added to `ChatOptions.Tools`), or both. This is how you inject runtime state into the model without hardcoding it.

The `InvokingContext` passed to each provider contains: the `AgentSession` (per-agent state bag), the accumulated `AIContext` built by prior providers (so later providers can see what was already injected), and the current message list.

### 8.1 UserContextProvider

Reads `UserContext.Email` and `UserContext.Name` from an `AsyncLocal` — set by `UseAgentRequestContext()` from the JWT `name` and `preferred_username` claims. Injects a `## User` system block every turn. This is how the ticket agent knows which email to use as `requestedBy`, and how "assign to me" resolves to an actual email address.

### 8.2 LongTermMemoryContextProvider

Reads the user's `LongTermUserProfile` from Redis at `ltm:{email.toLower()}:profile`. The profile stores: `Email`, `Name`, `LastSeenAt`, and up to 10 free-text `Preferences` (e.g. "prefers concise responses", "always uses dark mode"). Preferences are accumulated via `UpsertPreferenceAsync` when the agent learns something persistent about the user. Injected as a `## User Memory` block. TTL is configurable (long — the intent is months, not hours).

**What "long-term" means in this context:** Not long relative to a conversation, but long relative to Redis's usual ephemeral use. It survives across sessions because it's keyed by email, not threadId. It is cleared on Redis restart (alpine, no persistence), which is why `-ClearLongTermMemory` is a separate flag in the cleanup script.

### 8.3 TurnGuardContextProvider

Reads `TurnStateContext` — two `AsyncLocal` values updated by `RetryingMcpTool.InvokeCoreAsync` every time a tool is called:
- `ToolCounts` — `ConcurrentDictionary<string, int>` of tool name → call count this turn
- `LastUserMessage` — the raw user message text

Injects two optional blocks:

**`## Current Turn Signals`** — if the user message contains retry keywords ("retry", "continue", "still broken"), urgency keywords ("urgent", "asap", "critical", "blocked", "frustrated"), or broad-impact keywords ("whole team", "everyone", "multiple users"), injects behavioural guidance: continue incomplete workflow, match priority to urgency, correlate with incident context.

**`## Current Turn Tool History`** — lists how many times each tool was called this turn. Adds explicit rules: "status tools already ran — don't call again", "ticket already created — reuse it", "KB article already indexed — reuse it". This prevents the model from retrying completed side effects within a turn.

### 8.4 AzureAiSearchContextProvider (RAG)

Takes the latest user message, sends it to Azure AI Search as a hybrid semantic/vector query, and injects the top results as a `## RAG Context` system block before the model is called. The agent already has the most relevant KB articles in context before it even decides whether to call `search_kb_articles`. Uses an 8-second independent timeout (not the raw request cancellation token) so a dropped SSE connection from the previous turn doesn't cancel RAG gathering for the next turn.

### 8.5 AttachmentContextProvider

Reads processed attachment data from `attachments:{threadId}` in Redis. Two modes:

- `peek: true` (v2 orchestrator): `LoadAsync` — reads without deleting. The orchestrator sees the attachment, recognises it needs diagnostic analysis, and routes to the diagnostic specialist.
- `peek: false` (v1 agent, v2 diagnostic agent): `LoadAndClearAsync` — reads and deletes atomically. The attachment is consumed exactly once. If the diagnostic agent is incorrectly invoked a second time with no attachment in context, it silently calls its handoff function without writing anything (per its rules).
- `suppressRenderHint: false` (v1): appends `[FIRST ACTION REQUIRED]: call show_attachment_preview(...)` so the agent triggers the UI preview card immediately.
- `suppressRenderHint: true` (v2): omits the render hint — `show_attachment_preview` is a CopilotKit frontend tool not available inside workflow child agents.

Text attachments are injected as `ChatRole.System` messages with the extracted content (capped at 5,000 characters ≈ 1,100 tokens). Images are injected as `ChatRole.User` messages with `DataContent` (base64 bytes + MIME type) for vision model processing.

### 8.6 DynamicToolSelectionProvider

**Single-agent (v1) mode:** Embeds the user's message using `text-embedding-3-small`, computes cosine similarity against pre-embedded tool descriptions, and injects the top-K most relevant tools into `AIContext.Tools`. Bounded result cache of 200 entries per session. Falls back to all tools if embedding fails (8-second timeout) or if total tool count is ≤ topK (no point ranking 5 tools when you need 5).

**Specialist (v2) mode:** When `allowedTools` is set (e.g. `TicketAgentFactory.AllowedTools = ["create_ticket", "get_ticket", ...]`), the provider returns *all* allowed tools without any embedding or ranking. This is a deliberate design decision: a specialist has a small, well-defined tool set. Semantic ranking on a set of 5–6 tools adds noise and risks dropping a critical tool (e.g. ranking `search_tickets` below `create_ticket` when the task is to check for duplicates).

All `DynamicToolSelectionProvider` instances share the same `Task<IReadOnlyList<(AIFunction, float[])>>` from `toolIndexTcs`. Tool embeddings are computed once at startup (via `ApplicationStarted`) and reused across all providers. First-turn requests await this task with a 60-second guard.

### 8.7 FrontendToolForwardingProvider

CopilotKit injects its frontend tools (like `show_ticket_created`, `show_attachment_preview`) into `AgentRunOptions.ChatOptions.Tools` in the AG-UI request. These render tools are what drive UI card rendering — the model calls them and CopilotKit renders the matching React component. `WorkflowHostAgent` drops them when delegating to child agents (it sets `ChatOptions = null` on specialists). `WorkflowAgentWrapperFactory` captures them into this provider's `AsyncLocal` before delegating; `ThreadIdPreservingChatClient` restores them if the `AsyncLocal` was lost across a task boundary.

### 8.8 AgentSkillsProvider

Loads SKILL.md files from the `skills/` directory at startup. There are 5 skills:

- `escalation-protocol` — multi-tier escalation paths
- `frustrated-user` — empathy-first response patterns
- `major-incident` — war-room coordination workflow
- `security-incident` — security response runbook
- `vip-request` — white-glove treatment for executives

Each skill is advertised as a one-line summary in the system prompt (~100 tokens total for all 5). When the agent decides a skill is relevant to the current conversation, it calls `load_skill("skill-name")`, which reads the full SKILL.md and injects it as a system message. Progressive loading — the model only pays the full context cost for the skills it actually needs.

---

## 9. Chat History — RedisChatHistoryProvider

`ChatHistoryProvider` is a MAF abstraction with two lifecycle methods: `ProvideChatHistoryAsync` (before the turn — load history) and `StoreChatHistoryAsync` (after the turn — persist new messages). Only the v1 agent has one attached. In v2, the workflow runtime owns and manages the full message list across all handoffs — no individual specialist manages history.

**Key:** `messages:{threadId}`. Stores a JSON array of `{role, content}` objects. Only `user` and `assistant` messages with non-empty text are persisted — tool call/result messages are not stored (they'd poison the history with raw JSON payloads the model shouldn't see on next turn).

**`SummarizingChatReducer`:** Called on every history load. If the message count exceeds `SummarisationThreshold` (configurable), it calls the LLM to summarise all but the most recent `TailMessagesToKeep` messages into a single "Summary of prior conversation: ..." block, then prepends it to the verbatim tail. This prevents context windows from growing unbounded across long conversations. The verbatim tail is kept because recent context is where the model performs best — summarising the last 3 messages would lose precision.

**Graceful degradation:** If Redis is unreachable, `ProvideChatHistoryAsync` returns an empty list (the turn starts fresh, no crash) and `StoreChatHistoryAsync` swallows the exception (history not persisted for this turn, but the response was already streamed). Redis unavailability degrades the experience gracefully rather than breaking it.

---

## 10. MCP — Model Context Protocol

MCP is an open protocol for connecting AI models to tools and data sources. In this solution:

- **McpServer** is decorated with `[McpServerToolType]` / `[McpServerTool]` attributes on static methods. `ModelContextProtocol.AspNetCore` handles tool discovery (listing), argument deserialisation, DI injection, and result serialisation over HTTP Streamable transport.
- **AgentHost** connects at startup as an MCP client, discovers all tools as `AIFunction` instances, and makes them available in `ChatOptions.Tools`.

**Why MCP instead of direct function calls in the AgentHost?** The McpServer is a separate deployable unit. The tools — Cosmos reads/writes, AI Search operations, status queries — are pure business logic with no coupling to MAF, streaming, or agent context. They can be tested, scaled, redeployed, or replaced without touching the AgentHost. The MCP protocol provides the contract.

**`McpToolsProvider`:** Maintains a live `McpClient` (from `ModelContextProtocol` 1.2.0) connected over `HttpClientTransport`. Session TTL is 3 minutes — proactively reconnects before Azure Container Apps' 240-second SSE idle connection timeout kills the channel. Uses a `SemaphoreSlim(1,1)` for double-checked locking on reconnect. `GetCachedToolOrDefault(name)` is a lock-free read (managed reference reads are atomic in .NET) used by `RetryingMcpTool` to pick up fresh functions after a sibling reconnected.

**`RetryingMcpTool`:** Wraps each `AIFunction` from the McpServer. On call failure due to transport errors (HTTP failure, session expiry, `TaskCanceledException` on idle cut, `InvalidOperationException` about closed transport), it calls `provider.RefreshAsync()` to reconnect and retries once with the replacement function. All sibling wrappers automatically use the new session on their next call via the shared provider cache — no explicit coordination. Also calls `TurnStateContext.IncrementToolCount(toolName)` so the `TurnGuardContextProvider` can track in-turn usage.

---

## 11. RetrySafeSideEffectTool + ThreadSideEffectStore

**The problem:** The LLM can attempt the same tool call more than once in a turn — if a streaming response is interrupted mid-way, the context window shows a `FunctionCallContent` without a corresponding `ToolResultContent`, and the model attempts to complete it again on resume. For writes like `create_ticket` and `index_kb_article`, this means duplicates.

**The solution:** `RetrySafeSideEffectTool` wraps only these two tools. Before each invocation, it builds a deterministic `operationKey` by normalising arguments (lower-case, whitespace-collapsed) and SHA-256 hashing them:
- `create_ticket`: hash of `toolName | title | description | category | requestedBy`
- `index_kb_article`: hash of `toolName | title | SHA-256(content) | category`

The Redis key is `sideeffect:{threadId}:{operationKey}`.

State machine stored in Redis:
1. Key absent → `TrySetAsync` (NX — set only if not exists, handles concurrent callers); mark `Pending`, execute tool, mark `Completed` with result payload
2. Key exists as `Completed` → return cached `ResultPayload` without calling the tool
3. Key exists as `Pending` → poll every 300ms for up to 20 attempts (6 seconds total), then return when completed or a "pending" stub response

The `TrySetAsync` (Redis `SET NX`) handles the race between two concurrent callers starting simultaneously — only one wins the write, the other reads the Pending state and waits.

**Cross-session limitation:** The key includes `threadId`. A new browser session has a new threadId — the store is empty, and a ticket will be re-created for the same document. For `index_kb_article`, the KB service has its own content-based dedup before indexing. For `create_ticket`, the ticket agent instructions require calling `search_tickets` first when the user's prompt includes conditional language ("if not already present", "if one does not already exist").

---

## 12. MAF — Microsoft Agents Framework (Deep Dive)

MAF is the framework that turns `IChatClient` + context providers + history into a production-ready conversational agent. The key abstraction is `AIAgent` (with `RunAsync` / `RunStreamingAsync`) and the building block is `ChatClientAgent`.

### 12.1 ChatClientAgent

`chatClient.AsAIAgent(options)` (v1) or `new ChatClientAgent(chatClient, options)` (v2 specialists) creates a `ChatClientAgent`. On each `RunStreamingAsync` call, it:
1. Calls each `AIContextProvider` in registration order — accumulates system messages and tools into `AIContext`
2. Calls `ChatHistoryProvider.ProvideChatHistoryAsync` (if one is attached) — loads conversation history
3. Merges everything: system messages from providers + history + current turn messages → full `IEnumerable<ChatMessage>`
4. Calls `IChatClient.GetStreamingResponseAsync(messages, chatOptions)` — streams model response
5. Invokes `FunctionInvokingChatClient` logic (either external in v1 pipeline or MAF-internal in v2) to handle tool calls
6. Calls `ChatHistoryProvider.StoreChatHistoryAsync` — persists new messages

### 12.2 OpenTelemetryAgent

Wraps any `AIAgent` to emit `invoke_agent {name}` OpenTelemetry spans. In `HelpdeskWorkflowFactory.BuildWorkflow()`, each of the 5 agents (orchestrator + 4 specialists) is individually wrapped so App Insights shows per-specialist spans as children of the top-level v2 span. `EnableSensitiveData = true` includes message content in traces; `false` (default, production setting) captures only metadata (agent name, model, token counts).

### 12.3 HandoffWorkflowBuilder

`AgentWorkflowBuilder.CreateHandoffBuilderWith(orchestrator)` creates a workflow where the orchestrator is the entry point. `.WithHandoffs(from, [to1, to2, ...])` declares directed edges.

At build time, MAF generates `AIFunctionDeclaration` objects for each target: `handoff_to_1`, `handoff_to_2`, etc. — sequential integers scoped to each agent's declared target list. These are declarations (no implementation code), just schema metadata. They appear in `ChatOptions.Tools` so the model can see and call them.

The `HandoffAgentExecutor` monitors the streaming output token by token. When it sees `FunctionCallContent` whose name matches a registered handoff function name, it routes to the corresponding target agent. When the stream completes without any handoff call, it routes to `handoffsEndExecutor` — the workflow terminates.

### 12.4 Why `transfer_to_*` was broken

Other frameworks (OpenAI Assistants, AutoGen) use agent-name-based handoff functions like `transfer_to_ticket_agent`. The agent instructions were originally written with this pattern. But MAF uses sequential integers: `handoff_to_1`, `handoff_to_2`. The model had `handoff_to_1` in its actual tool list but instructions saying to call `transfer_to_orchestrator`. Faced with a contradiction, it resolved it by narrating the handoff as plain text. Fix: all specific function names removed from instructions; agents are told to "use the handoff function — its description tells you what the target does."

### 12.5 HandoffInstructions

MAF appends its own handoff instructions to every agent's system prompt via `CreateConfiguredChatOptions`. The default says "if appropriate, you may call a handoff function" — permissive enough that agents finished their work and stayed silent without handing off. Overridden globally via `.WithHandoffInstructions(...)` with mandatory language: "you MUST call a handoff function — it is the ONLY way to signal completion." Each specialist also explicitly names `handoff_to_1` in its own instructions as a belt-and-suspenders measure.

### 12.6 HandoffToolCallFilteringBehavior

Controls which messages are stripped from the conversation history before each specialist receives it. Three modes:
- `HandoffOnly` (default, current setting): strips only the `handoff_to_*` function calls and their results from history. Domain tool results (`create_ticket`, `index_kb_article`) remain visible.
- `All`: strips ALL function calls and tool results. Was previously set. Caused duplicate tickets: specialists couldn't see that a ticket had already been created (its result was stripped), so they created another one.
- `None`: passes everything including handoff calls — model gets confused by seeing prior handoff results in its history.

### 12.7 WorkflowAgentWrapperFactory

`workflow.AsAIAgent("helpdesk-v2", ...)` wraps the `Workflow` in a `WorkflowHostAgent`. This is what gets registered with `app.MapAGUI("/agent/v2", ...)`. The `WorkflowAgentWrapperFactory` adds a middleware layer around `rawWorkflowAgent` that:
1. **Captures CopilotKit frontend tools** from `AgentRunOptions.ChatOptions.Tools` into `FrontendToolForwardingProvider` before delegating. `WorkflowHostAgent` passes `null` for `ChatOptions` to child agents — without this capture, render tools disappear.
2. **Converts `ErrorContent` to thrown exceptions**: `WorkflowHostAgent` catches specialist exceptions and wraps them in `ErrorContent`. The wrapper scans responses for content containing "blocked by Azure content safety" and re-throws a `ContentSafetyException` so the v2 safety recovery path works identically to v1.

### 12.8 The Handoff Loop

The full v2 request lifecycle for a multi-action prompt (e.g. "analyse this doc, add to KB, create and assign a ticket"):

```
AG-UI POST /agent/v2
  → WorkflowAgentWrapperFactory.RunStreamingAsync
    → WorkflowHostAgent.RunStreamingAsync
      → [Turn 1] Orchestrator: build queue [diagnostic → kb → ticket(create+assign)]
                               call handoff_to_4 (diagnostic)
        → DiagnosticAgent: analyse attachment, call handoff_to_1 (orchestrator)
      → [Turn 2] Orchestrator: diagnostic done; call handoff_to_2 (kb)
        → KBAgent: search + index article, call handoff_to_1 (orchestrator)
      → [Turn 3] Orchestrator: kb done; call handoff_to_3 (ticket)
        → TicketAgent: create ticket, assign ticket, call handoff_to_1 (orchestrator)
      → [Turn 4] Orchestrator: all tasks done; write final summary → stream to client
```

Each `handoff_to_N` call is executed by `HandoffAgentExecutor` without going back to the HTTP client — the whole multi-turn loop runs server-side and the client sees a single SSE stream for the entire compound operation.

---

## 13. The Two Routes Compared

| | v1 `/agent` | v2 `/agent/v2` |
|---|---|---|
| Runtime | Single `ChatClientAgent` | MAF `HandoffWorkflow` (5 agents) |
| Model | `gpt-5.3-chat` | `gpt-5.2-chat` |
| Chat history | `RedisChatHistoryProvider` on the agent | Workflow runtime owns it; no provider on specialists |
| `UseFunctionInvocation()` | In pipeline (v1) | Omitted; MAF inserts its own |
| Tool access | `DynamicToolSelectionProvider` (semantic top-K) | Per-specialist, all allowed tools returned |
| Attachment | `peek: false`, render hint on | Orchestrator `peek: true`, diagnostic `peek: false`, render hint off |
| Thread safety | Not needed (single async chain) | `ThreadIdPreservingChatClient` guards AsyncLocal across `SuppressFlow` |
| Frontend tools | Captured from `ChatOptions.Tools` naturally | `WorkflowAgentWrapperFactory` + `ThreadIdPreservingChatClient` |
| Auth | Entra JWT + `/agent/demo` (no auth) | Entra JWT only |
| Telemetry | Single `invoke_agent` span | Per-specialist child spans under top-level v2 span |

---

## 14. Azure Services in Detail

**Azure OpenAI:** Two separate model deployments. `gpt-5.3-chat` for v1 (single-agent, full tool set, render tool management). `gpt-5.2-chat` for v2 (workflow specialists, focused tasks). Separate deployments mean independent rate limit quotas, context window configuration, and model version pinning. `DefaultAzureCredential` (Managed Identity) in production; API key in dev.

**Azure AI Search:** Hybrid semantic + vector KB index named `helpdesk-kb`. KB articles are chunked, embedded with `text-embedding-3-small`, and indexed with title/category/content fields. Two access patterns: (1) RAG — `AzureAiSearchContextProvider` queries with the user message, injects top results as system context before the model call; (2) Tool — model explicitly calls `search_kb_articles` for structured browse. The index is updated by `index_kb_article` tool calls from the model.

**Azure Cosmos DB:** IT support tickets. Partitioned by ticket ID. Sequential INC-XXXX IDs via `Interlocked.Increment` on a singleton `_nextId` counter — safe because McpServer is constrained to `minReplicas=1` in Bicep. Cosmos Patch API used for status updates and comment appends to avoid full-document rewrites. `CosmosStjSerializer` (System.Text.Json) replaces the default Newtonsoft.Json serialiser internally.

**Azure Blob Storage:** Two containers — `helpdesk-attachments` (user uploads, cleared on cleanup) and `eval-results` (evaluation JSON outputs). Attachments flow: browser POST → AgentHost `/attachments` endpoint → `DocumentIntelligenceService` extracts text → `BlobStorageService` stores the file → `RedisAttachmentStore` stores the extracted text + blob URL keyed by `threadId`.

**Azure Document Intelligence:** Processes uploaded PDFs and Office documents. Returns structured text from the `AnalyzeDocument` API (prebuilt-read model). Text is capped at 5,000 characters per attachment (≈1,100 tokens — enough for 2–3 pages). Images bypass Document Intelligence and are passed directly as base64 `DataContent` to the vision model.

**Redis (Container App, `redis:7-alpine`):** Key namespaces:
- `messages:{threadId}` — chat history (JSON array)
- `attachments:{threadId}` — processed attachment (JSON with text + base64 + blobUrl)
- `sideeffect:{threadId}:{opKey}` — dedup ledger (JSON state object)
- `usage:{threadId}:latest` — last turn token counts (JSON)
- `ltm:{email}:profile` — long-term user profile (JSON)

`ConnectionMultiplexer` configured with `abortConnect=false` (app starts even if Redis isn't ready), `syncTimeout=15s`, `keepAlive=60s` (prevents Azure TCP proxy from dropping idle connections).

**Azure Monitor / OpenTelemetry:** `AddOpenTelemetry().UseAzureMonitor()` sends traces and metrics. MEAI's `.UseOpenTelemetry()` emits `gen_ai.*` semantic conventions (model, token counts, input/output messages if `EnableSensitiveData=true`). MAF's `OpenTelemetryAgent` emits `invoke_agent` spans. Custom traces and metrics registered via `AgentHostCompositionExtensions`. The "Agents (Preview)" blade in App Insights shows the per-specialist MAF workflow as a tree of spans.

**Entra ID (Microsoft Entra / Azure AD):** JWT Bearer on `/agent` and `/agent/v2`. The Next.js frontend uses `next-auth` with MSAL to acquire an access token from Entra, which is sent as `Authorization: Bearer` on every AG-UI request. The AgentHost validates audience (`api://{clientId}`) and issuer (`login.microsoftonline.com/{tenantId}/v2.0`). `/agent/demo` skips auth for conference demo convenience.

---

## 15. Frontend Stack

**Next.js 16.2.2 + React 19.2.4 + TypeScript 5.9.3.** Running with Turbopack in dev (`next dev --turbopack`).

**CopilotKit 1.54.1 + @ag-ui/client 0.0.50.** CopilotKit is a React library built on top of the AG-UI protocol. `<CopilotKit runtimeUrl="/api/copilotkit">` configures the AG-UI endpoint — requests go to the Next.js API route which proxies to the AgentHost, keeping the backend URL out of the browser. `<CopilotChat>` renders the chat UI and handles all SSE stream management.

**`useCopilotAction`:** Registers frontend tools the agent can call. Each action has a name, parameter schema, and a React handler. When the model calls `show_ticket_created({ id, title, priority, ... })`, CopilotKit invokes the registered handler which renders a custom ticket card component inline in the chat. These are the `_renderAction` / `_renderArgs` fields embedded in every MCP tool JSON response — the tool result tells the model which frontend function to call and with what arguments.

**`next-auth`:** Handles MSAL authentication. On sign-in, acquires an Entra access token scoped to `api://{clientId}`. The token is attached to every AG-UI request. `next-auth` manages token refresh, session persistence, and the PKCE flow.

---

## 16. Production Considerations

**Idempotency of every write:** LLMs retry. Networks drop. Users hit refresh. Any database write must be safe to call twice with identical arguments. `RetrySafeSideEffectTool` wraps `create_ticket` and `index_kb_article`. For any new agentic write operation you add: decide upfront whether it's naturally idempotent (upsert semantics) or needs a guard layer.

**`AsyncLocal` across framework task boundaries:** `ExecutionContext.SuppressFlow()` is used by MAF, `Task.Run`, `Thread.Start`, and some middleware. Any ambient value in `AsyncLocal` — threadId, tenant ID, user context — becomes `null` on the other side silently. You need explicit capture-and-restore wherever a framework spawns tasks with suppressed flow. Fail to do this and you get subtle bugs: wrong Redis keys, null user context, missing tool selections.

**Context window management:** A naive growing history eventually hits the model's token limit and starts returning 400 errors. `SummarizingChatReducer`: keep the verbatim tail (where the model performs best), summarise the older head. Tune `TailMessagesToKeep` conservatively — too aggressive summarisation loses precision on recent events.

**MCP transport resilience:** Long-lived SSE channels get killed by network idle timeouts (Azure Container Apps: 240 seconds). Proactively reconnect at 3 minutes. Wrap every MCP tool call with a single-retry-on-transport-error pattern — don't let a session expiry surface as a user-visible error.

**Dynamic tool selection vs. full injection:** Injecting 20 tool descriptions every turn costs tokens and increases the chance of the wrong tool being called. Embedding-based selection (top-K by cosine similarity) is cheaper and more accurate for a general-purpose agent. For specialist agents with a small, well-defined tool set, skip semantic ranking entirely — return all allowed tools, no noise.

**Content safety as pipeline infrastructure:** The guard is in the `IChatClient` pipeline, not in any individual agent. This means it's guaranteed regardless of which route, which agent, or which workflow path executes. The critical operational detail: clear the Redis history after a filter hit. Without that, a poisoned thread loops forever.

**Single-replica constraint for sequential IDs:** `Interlocked.Increment` for ticket IDs only works because McpServer runs as one replica. This is fine for a demo. Production requires distributed ID generation — Cosmos atomic counter documents, Service Bus sequence numbers, or a UUIDs-based approach.

**Observability key:** Tag everything with `threadId`. It's the correlation key that ties together App Insights traces, Redis state, Cosmos audit logs, and the AG-UI session. Without it, diagnosing a production issue means guessing across disconnected signals.

---

*All details above are derived from the actual HelpdeskAI.CSharp codebase — no assumptions or generalisations.*
