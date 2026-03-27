# Changelog

All notable changes to HelpdeskAI are recorded here.

---

## [Unreleased] - 2026-03-26

### Added

- **V2 multi-agent workflow** — MAF `HandoffsWorkflow` with orchestrator + 4 specialist agents (diagnostic, ticket, KB, incident) at `/agent/v2`. Each specialist has scoped MCP tools and tailored system prompts. The orchestrator routes user requests to the appropriate specialist and chains multi-step tasks automatically.
  - `OrchestratorAgentFactory` — routes to specialists, handles multi-step chaining
  - `DiagnosticAgentFactory` — attachment analysis, incident diagnosis, triage
  - `TicketAgentFactory` — ticket creation, assignment, status updates
  - `KBAgentFactory` — knowledge base search and article indexing
  - `IncidentAgentFactory` — system status checks and incident impact analysis
  - `HelpdeskWorkflowFactory` — assembles the workflow with all providers and tools
- **Agent mode toggle** — Settings page toggle to switch between v1 (single agent) and v2 (multi-agent). Preference stored in a cookie; frontend routes to the appropriate AG-UI endpoint.
- **CopilotKit frontend tool forwarding (v2)** — `FrontendToolForwardingProvider` + `AIAgentBuilder.Use()` middleware captures CopilotKit action tools from `AgentRunOptions` before `WorkflowHostAgent` strips them, making render-action cards (show_ticket_created, etc.) work inside the workflow.
- **Attachment peek/clear pattern (v2)** — orchestrator uses a peek provider (reads without clearing) while diagnostic_agent uses a clearing provider, preventing infinite routing loops.
- **`FrontendToolCapturingChatClient`** — IChatClient pipeline middleware that captures frontend tools from `ChatOptions.Tools`.
- **`ThreadIdPreservingChatClient`** — preserves `AsyncLocal<string>` thread ID across async boundaries in the IChatClient pipeline.
- **App Insights Agents (Preview)** — custom `ActivitySource` emitting `invoke_agent` spans with `gen_ai.operation.name`, `gen_ai.agent.name`, `gen_ai.agent.id`, `gen_ai.system` semantic attributes for the Azure Monitor Agents preview dashboard.
- **`CitationBadge.tsx`** — inline citation link component for KB article references in chat responses.
- **Upload logging** — diagnostic logging in both frontend upload route and AgentHost attachment endpoint.
- **`docs/regression-suite.md`** — route-by-route Azure regression checklist covering auth, tickets, incidents, KB, attachment workflows, memory, and cross-browser validation for both `v1` and `v2`.
- **`infra/cleanup-demo-data.ps1`** — cleanup utility that removes non-seed Cosmos tickets and AI Search KB artifacts while preserving repository seed data for repeatable regression runs.

### Changed

- **Specialist agent instructions** — all v2 specialists have explicit "execute immediately, never ask for confirmation" directives to prevent passive behaviour in multi-step workflows.
- **Attachment context provider** — now supports a `peek` constructor parameter; `peek: true` reads from Redis without clearing, `peek: false` (default) clears after read.
- **`RedisAttachmentStore` logging** — upgraded from Debug to Information level with SAVED/PEEK/CONSUME markers for attachment lifecycle traceability.

### Fixed

- **V2 attachment context race condition** — MAF resolves all agents' `AIContextProviders` simultaneously at workflow start; diagnostic_agent's clearing provider consumed attachments before orchestrator could peek. Fixed by separating peek (orchestrator) and clear (diagnostic) providers.
- **V2 specialists refusing to act** — diagnostic_agent instructions contained "you have no access to live systems" language causing the agent to treat interactions as simulations. Removed and replaced with explicit action scope.

---

## [Unreleased] - 2026-03-23

### Added

- **`docs/model-compatibility.md`** — documents the three model dependency layers (`_renderAction` follow-through, multi-step instruction following, tool calling), the compatibility matrix (gpt-4o ✅, gpt-4o-mini ✅, gpt-4-turbo ✅, model-router ⚠️, gpt-5.2-chat ❌), and configuration instructions for switching models.
- **Settings page model compatibility card** (`HelpdeskChat.tsx`) — displays the active Azure OpenAI model and a link to `docs/model-compatibility.md`; updates the About card to show `Azure OpenAI (gpt-4o)` and `Azure AI Search (Basic tier)`.

### Changed

- **Chat model switched to `gpt-4o`** — migrated from `model-router` (→ `gpt-5.2-chat` tested and rejected) to `gpt-4o` as the production chat deployment. `gpt-5.2-chat` was found to ignore `_renderAction` instructions embedded in MCP tool results, producing text summaries instead of calling frontend render tools. `gpt-4o` follows `_renderAction` reliably.
- **`DynamicTools.TopK` raised from 5 → 8** (`appsettings.json`) — ensures all workflow tools including `assign_ticket` are always in the selected tool set for multi-step agentic turns.
- **`DynamicToolSelectionProvider` turn-cache** — added a `ConcurrentDictionary<string, AIContext>` keyed on the user query string so embedding + ranking runs once per unique query per turn rather than on every intermediate LLM inference step. Cache auto-clears at 200 entries.
- **`BaseInstructions` render pairing table** (`HelpdeskAgentFactory.cs`) — added an explicit 8-row table mapping each MCP tool to its required frontend render tool (e.g. `create_ticket → show_ticket_created`). Agent reads it at turn start as part of its plan rather than inferring render calls mid-turn.
- **`BaseInstructions` sequential tool call rule** (`HelpdeskAgentFactory.cs`) — added "Always call tools sequentially, one at a time. Never call multiple tools in parallel." to `## Tool Rules` to prevent parallel invocations where missing optional parameters cause hard exceptions.
### Fixed

- **`create_ticket` missing `priority` on parallel calls** (`TicketTools.cs`) — `priority` parameter now defaults to `"Medium"` if not provided, preventing a hard `ArgumentException` when gpt-4o calls `create_ticket` in a parallel tool batch without specifying priority.
- **KB article card not rendering** (`KnowledgeBaseTools.cs`) — `index_kb_article` `_renderArgs` was missing the `content` field (stripped in a previous session for token reduction), causing `show_kb_article` to render a blank card. `content` restored to `_renderArgs`.
- **`get_active_incidents` auto-called proactively** — render pairing table clarified so the `get_active_incidents → show_incident_alert` entry is not misread as a trigger to call the tool proactively; Tool Rules already constrains it to explicit user requests only.

---

## [Unreleased] - 2026-03-20

### Added

- **Response token stats chip** (`HelpdeskChat.tsx`) — renders `⏱ Xs · 📥 N in / 📤 M out` in the header row after each agent response. Uses a `fetchStatsRef` pattern to avoid stale closures, exponential-backoff retry (200 ms → 4 s), and clears on the next send.
- **`IncludeStreamingUsagePolicy`** (`AgentHost/Infrastructure/`) — `System.ClientModel.Primitives.PipelinePolicy` that injects `stream_options: { include_usage: true }` into every streaming Azure OpenAI chat completion request. Fixes null `aggregated.Usage` in `UsageCapturingChatClient` (Azure omits token counts from streaming SSE unless explicitly requested).
- **`/agent/usage` endpoint** — `GET /agent/usage?threadId=` returns `{ promptTokens, completionTokens }` from the thread-scoped Redis key.
- **`DynamicToolSelectionProvider`** — per-turn cosine similarity tool selection via `text-embedding-3-small`; injects only the top-K most relevant MCP tools per turn instead of the full set.
- **`AttachmentContextProvider`** — injects staged attachment text/OCR into each agent turn; scoped per session via `IAttachmentStore` (Redis one-shot staging, 1-hour TTL).
- **`RetryingMcpTool`** — `DelegatingAIFunction` wrapper; catches MCP `Session not found` (-32001) errors, reconnects, and retries once transparently.
- **`AGUIHistoryNormalizingClient`** — merges consecutive assistant tool-call messages before they reach the Azure OpenAI API (parallel tool-call compatibility).
- **Polly resilience pipeline** on the `McpServer` `HttpClient` — 3 retries with exponential backoff, 30 s total timeout.
- **Textarea auto-grow + scrollbar** (`HelpdeskChat.tsx`) — input expands to 160 px then shows a scrollbar; driven by React state (`textOverflow`) to avoid reconciliation conflicts with imperative DOM updates.

### Changed

- **`UsageCapturingChatClient`** — usage capture is now thread-bound only; the global fallback key was removed to avoid cross-session leakage.
- **Stats chip placement** — moved from a floating `position:absolute` overlay inside `.hd-chat-wrapper` (clipped by `overflow:hidden`) to the header row as a sibling of the page title; no DOM injection, pure React state.
- **`HelpdeskAgentFactory` system prompt** — added explicit numbered-task-list execution rule (execute ALL steps sequentially in one response; never pause between steps). Added field-by-field mapping for `show_ticket_details`: agent must call `get_ticket` first and extract all fields (id, title, description, priority, category, status, assignedTo, createdAt) from the text response.
- **`HelpdeskActions`** — 7 render actions (added `show_ticket_details`, `show_kb_article`, `suggest_related_articles`, `show_attachment_preview` alongside the original 3).
- **`Program.cs`** — `AzureOpenAIClient` for chat now created with `AzureOpenAIClientOptions` carrying `IncludeStreamingUsagePolicy`; embedding client unchanged.
- **`.claude/launch.json`** — AgentHost launch config uses `--no-build` so `preview_start` always runs the explicitly pre-built binary.
- **NuGet versions bumped** — `StackExchange.Redis` 2.11.8 → 2.12.1, `Microsoft.Extensions.AI` / `OpenAI` 10.3.0 → 10.4.0, `ModelContextProtocol` 1.0.0 → 1.1.0, MAF rc2 → rc4.
- **npm versions bumped** — `@copilotkit/*` 1.52 → 1.54, `@ag-ui/*` 0.0.45 → 0.0.47.

### Removed

- **UsageContext.cs** (UsageStore + UsageSnapshot) - dead code; completely superseded by direct Redis writes in UsageCapturingChatClient. Zero external references confirmed before deletion.
- **DEMO.md** - retired in favor of keeping the richer walkthrough and demo guidance in the root README.md.

### Fixed

- **MCP session expiry** — `McpToolsProvider.RefreshAsync` now uses a lock to prevent concurrent reconnect races that disposed an active client mid-use.
- **AI Search transient failures** — retry policy added to the `McpServer` `HttpClient`; `AzureAiSearchService` handles transient 503/429 responses gracefully.
- **Ticket detail card blank** — `show_ticket_details` prompt updated to require `get_ticket` before rendering; field extraction from the tool's text response made explicit so title/description/status are never omitted.
- **CopilotKit frontend instructions duplication** — removed the duplicate `instructions` prop from `<CopilotChat>`; `BaseInstructions` in `HelpdeskAgentFactory` is the single source of truth.

---

## Earlier History

See `git log` for full commit history prior to this changelog.

