# Changelog

All notable changes to HelpdeskAI are recorded here.

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


