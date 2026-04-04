# Changelog

All notable changes to HelpdeskAI are recorded here.

---

## [Unreleased] - 2026-04-04 (Conference-Safe MAF v1 Upgrade)

### Added

- **Conference-safe solution package upgrade pass** — HelpdeskAI is now aligned to the official .NET Microsoft Agents Framework v1 core package line: `Microsoft.Agents.AI`, `Microsoft.Agents.AI.OpenAI`, and `Microsoft.Agents.AI.Workflows` are on `1.0.0`, while `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` remains on the latest compatible AG-UI hosting preview companion (`1.0.0-preview.260311.1`).
- **Frontend/runtime dependency refresh** — Next.js `16.2.2`, CopilotKit `1.54.1`, AG-UI client/core `0.0.50`, and `@types/node` `25.5.2`.
- **Test/tooling dependency refresh** — `Microsoft.NET.Test.Sdk` `18.3.0`, `MSTest.TestAdapter` `4.1.0`, `MSTest.TestFramework` `4.1.0`, and `StackExchange.Redis` `2.12.14`.

### Changed

- **Stable MAF API migration** — agent skills now use `AgentSkillsProvider` on the stable MAF v1 core package line instead of the old preview-era `FileAgentSkillsProvider` type name.

---

## [Unreleased] - 2026-04-03 (Telemetry Enrichment + Azure Monitor Workbook)

### Added

- **`ThreadIdEnrichingProcessor`** — OTel `BaseProcessor<Activity>` registered in `AddHelpdeskTracing()`. Fires on every span start and stamps `thread.id` (from `ThreadIdContext.Current`) and `enduser.id` (from `UserContext.Email`) as span tags. After this change, every `invoke_agent` span — across all turns of a conversation — carries the conversation's threadId as a filterable custom dimension in App Insights.
- **Azure Monitor Workbook** (`infra/workbooks/helpdesk-ai-monitoring.json`) — parameterized 6-panel workbook deployed to the same resource group as App Insights. Panels:
  1. **Conversation Trace** — all `invoke_agent` spans for a selected `thread.id`, ordered by timestamp
  2. **Agent Routing (V2)** — pie chart + table of specialist invocation counts and average duration
  3. **V1 vs V2 Throughput** — request volume, p50/p95 latency, and error count side by side
  4. **Token Usage** — top 10 conversations by total prompt+completion token consumption
  5. **Eval Run Quality** — pass/fail counts per execution run + time-series bar chart
  6. **Error Rate** — exceptions and failed dependency spans as a time-series line chart
- **Workbook deploy command** — `az deployment group create --template-file infra/workbooks/helpdesk-ai-monitoring.json` — documented in `infra/README.md`.

### Changed

- **`AgentHostCompositionExtensions.cs`** — added `using System.Diagnostics` and `using OpenTelemetry`; `AddHelpdeskTracing()` now registers `ThreadIdEnrichingProcessor` before adding activity sources.

### KQL — Conversation correlation query

```kql
dependencies
| where customDimensions["thread.id"] == "<your-threadId>"
| project timestamp, name, duration, customDimensions["gen_ai.agent.name"], success
| order by timestamp asc
```

---

## [Unreleased] - 2026-04-02 (M4 — V2 Eval Coverage)

### Added

- **5 v2 eval scenarios** (Test16–Test20) targeting the multi-agent orchestrator workflow: `Test16_V2_TicketRouting`, `Test17_V2_KbRouting`, `Test18_V2_IncidentRouting`, `Test19_V2_MultiStep_TicketThenKb`, `Test20_V2_MultiTurn_SearchThenClose`. Eval suite now covers 20 scenarios (15 v1 + 5 v2).
- **`/agent/eval-v2` endpoint** — maps the same `wrappedWorkflowAgent` with X-Eval-Key auth (no Entra). Used exclusively by `EvalRunnerService` for v2 scenario runs via HTTP loopback. Only registered when `Evaluation:ApiKey` is configured.
- **V2 SSE loopback eval** — `EvalRunnerService.RunV2ScenarioAsync` posts AG-UI requests to `/agent/eval-v2`, parses the SSE stream (`TEXT_MESSAGE_CONTENT` delta events → assembled assistant text), and evaluates with the same `CompositeEvaluator` as v1.
- **V1/V2 route badge** in the Evaluations page scenario table — blue `V1` or purple `V2` badge in each row's Scenario column derived from the scenario name (`Test\d+_V2_` prefix). Scenario label strips both `V2_` prefix variants correctly.
- **`-CleanBlobs` switch** in `infra/cleanup-demo-data.ps1` — deletes eval blobs from the `eval-results` container older than `BlobAgeDays` (default 30) days. Requires `-BlobConnectionString` or `$env:AZURE_STORAGE_CONNECTION_STRING`.
- **`AgentHost:BaseUrl` config key** — controls the loopback URL used by `EvalRunnerService` for v2 self-calls. Defaults to `http://localhost:5200`; override to `http://localhost:8080` in Azure Container Apps (Dockerfile port).

### Changed

- **`EvalRunnerService` constructor** — now accepts `evalApiKey` and `selfBaseUrl` (injected via DI factory in `Program.cs`). DI registration changed from `AddSingleton<EvalRunnerService>()` to a factory overload.
- **`ScenarioSpec` record** — added `bool IsV2 = false` flag to route scenarios to either the v1 direct-call path or the v2 HTTP loopback path.
- **`RunScenarioAsync` refactored** into `RunV1ScenarioAsync` and `RunV2ScenarioAsync` with shared `EvaluateAndBuildResultAsync` and `PersistResultAsync` helpers.
- **Redis LTM cleanup** — `-ClearLongTermMemory` switch in `infra/cleanup-demo-data.ps1` now targets `ltm:*` (all LTM keys) rather than the narrower `ltm:*:profile` pattern, ensuring evaluation-temp memory is also cleared.

---

## [Unreleased] - 2026-04-02

### Added

- **M3 — Eval Dashboard** — new **Evaluations** sidebar page in the frontend. Click **▶ Run Evals** to trigger a run of 15 golden scenarios; results auto-refresh every 8 s while in progress and are stored as JSON blobs in Azure Blob Storage (`eval-results` container). Pass/fail and per-metric ratings (IntentResolution, TaskAdherence, Relevance, Coherence) are displayed per scenario with color-coded badges. The table is mobile-responsive (horizontal scroll + Primary column hidden on ≤640 px).
- **`EvalRunnerService`** — background service that runs 15 eval scenarios against the live agent pipeline and persists `ScenarioResultDto` JSON blobs. Supports both single-turn and multi-turn scenarios (intermediate turns build real conversation history before the evaluated final turn).
- **`EvalResultsEndpoints`** — three blob-backed endpoints: `GET /agent/eval/results` (execution summaries), `GET /agent/eval/results/{executionName}` (scenario detail), `POST /agent/eval/run` (trigger). All guarded by `X-Eval-Key`.
- **3 new eval scenarios** (15 total, up from 12): `Test13_MultiTurn_VpnThenTicket`, `Test14_MultiTurn_SearchThenClose` (multi-turn TaskAdherence), `Test15_OutOfScope` (Coherence — agent should politely decline non-IT requests).
- **Demo endpoint** — `/demo` route exposes the app without Azure AD auth for internal sharing. Renders a yellow warning banner; uses the same v1 agent pipeline via `/api/copilotkit/demo`. Mobile-responsive with a scoped CSS override for the full-height shell.
- **`/api/eval-results` Next.js route** — frontend API proxy that forwards eval list/detail/run requests to AgentHost with `X-Eval-Key`, requiring an authenticated user session.

### Changed

- **Eval user identity** — `POST /agent/eval` and `EvalRunnerService` now inject a synthetic eval persona (`eval-user@contoso.com`, Engineering team) into the system prompt so ticket and search tools have a userId to work with. Fixes 10 previously failing TaskAdherence scenarios where tool calls silently returned empty results.
- **Pass/fail metric lookup** — fixed: MEAI metric names have spaces (`"Intent Resolution"`, `"Task Adherence"`) but the lookup was searching for `"IntentResolution"` / `"TaskAdherence"`. Now uses `IntentResolutionEvaluator.IntentResolutionMetricName` etc. (static constants) for exact dictionary lookup.
- **Hardcoded "12 scenarios" references removed** — button text, in-progress status, and final count now derive from the actual `total` field returned by the backend.
- **Package version: `Microsoft.Extensions.AI`** — 10.4.0 → 10.4.1 (AgentHost).
- **Package version: `Microsoft.Extensions.AI.OpenAI`** — 10.4.0 → 10.4.1 (AgentHost).

---

## [Unreleased] - 2026-04-01

### Added

- **MAF Agent Skills (M2)** — `FileAgentSkillsProvider` wired into V1 (`HelpdeskAgentFactory`), V2 orchestrator (`OrchestratorAgentFactory`), and V2 diagnostic specialist (`DiagnosticAgentFactory`). Skills use the [agentskills.io](https://agentskills.io/) progressive disclosure protocol: skill names/descriptions are advertised in the system prompt each turn; full body is loaded on demand via the `load_skill` tool. Skills path is configurable via `Skills:Path` in `appsettings.json` (defaults to `"skills"`, resolved against `AppContext.BaseDirectory`).
- **5 behavioral SKILL.md files** (`src/HelpdeskAI.AgentHost/skills/`):
  - `escalation-protocol` — when and how to escalate to L2/L3/management with communication templates
  - `frustrated-user` — de-escalation techniques, empathy-first response patterns, what to avoid
  - `major-incident` — P1/P2 response playbook: confirm scope, triage, communication cadence, post-incident steps
  - `security-incident` — phishing/breach/malware response: contain → report (ext. 9911) → preserve evidence → communicate
  - `vip-request` — white-glove handling for executives: speed over process, dedicated ownership, adjusted SLAs
- **Telemetry fix — `OpenTelemetryAgent` wrapping** — V1 `HelpdeskAgent` and V2 `helpdesk-v2` workflow agent are now wrapped with `OpenTelemetryAgent` so App Insights Agents (Preview) shows top-level `invoke_agent` spans. V2 specialists (orchestrator, ticket, KB, incident, diagnostic) are also individually wrapped inside `HelpdeskWorkflowFactory` for per-specialist span attribution.
- **Telemetry fix — wildcard source/meter names** — `TraceSourceNames` and `MeterNames` in `AgentHostCompositionExtensions` now use wildcard patterns (`"*Microsoft.Extensions.AI"`, `"*Microsoft.Extensions.Agents*"`) to capture both current `Experimental.*` prefixed names and future stable renames. Fixes empty Models section and Token Consumption charts in App Insights.
- **Telemetry fix — v2 pipeline OTel config** — v2 `IChatClient` pipeline `.UseOpenTelemetry()` now passes `EnableSensitiveData` consistently with v1.

### Changed

- **`HelpdeskWorkflowFactory.BuildWorkflow`** — added optional `AIContextProvider? skillsProvider` and `bool enableSensitiveData` parameters; all 5 agents wrapped with `OpenTelemetryAgent`.
- **`OrchestratorAgentFactory.Create`** / **`DiagnosticAgentFactory.Create`** / **`HelpdeskAgentFactory.Create`** — added optional `AIContextProvider? skillsProvider` parameter; injected as the last provider when non-null.
- **`Evaluation:ApiKey` config key + `X-Eval-Key` header guard** — `/agent/eval` endpoint is now enabled in any environment when `Evaluation:ApiKey` is set (non-empty). Callers send the key as `X-Eval-Key` header. Empty = endpoint not registered. Removes the `!IsProduction()` gate, enabling CI/CD and remote eval runs against the deployed Azure Container App. Test harness reads `EVAL_API_KEY` env var and injects it automatically; `EnsureConfigured()` requires it when `EVAL_AGENT_URL` points at a non-localhost host.
- **`Telemetry:EnableSensitiveData` config key** — replaces `!IsProduction()` guard for all `EnableSensitiveData` settings (IChatClient OTel pipeline × 2, OpenTelemetryAgent × 3 specialist wraps, `BuildWorkflow` param). Defaults to `false`; enable per-environment via Container App env var `Telemetry__EnableSensitiveData=true`. When `true`, captures `gen_ai.input.messages`, `gen_ai.output.messages`, and other PII-bearing span attributes in App Insights.
- **Skills `CopyToPublishDirectory`** — changed from `<None CopyToOutputDirectory>` to `<Content CopyToPublishDirectory>` so skill SKILL.md files are included in `dotnet publish -c Release` output and therefore in the Docker image. Previously skills were only available in local dev build output.
- **Package upgrades** (AgentHost): `ModelContextProtocol` 1.1.0 → 1.2.0, `StackExchange.Redis` 2.12.4 → 2.12.8, `Azure.Identity` 1.19.0 → 1.20.0.
- **Package upgrades** (McpServer): `Microsoft.Azure.Cosmos` 3.57.1 → 3.58.0, `ModelContextProtocol.AspNetCore` 1.1.0 → 1.2.0.
- **`<NoWarn>`** in `HelpdeskAI.AgentHost.csproj` — added `MAAI001` alongside existing `MEAI001` to suppress the evaluation-only diagnostic for `FileAgentSkillsProvider`.
- **`AgentHostCompositionExtensions.cs`** — removed `ActivitySource agentActivitySource` parameter from `UseAgentRequestContext` and removed `StartAgentInvocationSpan` helper; `OpenTelemetryAgent` now owns all agent-level spans.
- **`EvalEndpoints.cs`** — added `X-Eval-Key` header validation; `MapEvalEndpoints` now takes the configured API key and rejects requests with wrong/missing key with 401.
- **`EvalHarness.cs`** (test project) — added `EVAL_API_KEY` env var support; `CallEvalAsync` sends `X-Eval-Key` header when set; `EnsureConfigured()` requires the key when targeting non-localhost.

### Fixed

- **App Insights Models section empty** — was caused by wrong meter/source names (`"Microsoft.Extensions.AI"` instead of `"Experimental.Microsoft.Extensions.AI"`). Fixed by wildcard patterns.
- **App Insights Token Consumption charts empty** — `gen_ai.client.token.usage` metric was not reaching the meter pipeline. Fixed by wildcard meter registration.
- **Duplicate `invoke_agent` spans** — custom `ActivitySource` hand-rolling in `UseAgentRequestContext` was emitting manual spans alongside MAF's own. Removed custom span; `OpenTelemetryAgent` is the sole emitter.

---

## [Unreleased] - 2026-03-31

### Added

- **`AgentHostCompositionExtensions.cs`** — extracted request middleware composition (user context injection, turn state wiring, telemetry scope) out of `Program.cs` into a dedicated extension class, reducing startup file size and improving testability.

### Changed

- **`RetrySafeSideEffectTool`** — replaced magic-number wait loop with named constants (`PendingWaitAttempts`, `PendingWaitDelay`); `BuildPendingResponse` now returns structured JSON with `status`, `operationKey`, and `message` fields for easier debugging.
- **`FrontendToolForwardingProvider`** — refined tool capture and clear lifecycle to reduce stale tool state across workflow turns.
- **`ThreadIdPreservingChatClient`** — minor guard improvements for AsyncLocal restoration logging.
- **`TicketService`** — expanded seed ticket data for more realistic demo scenarios.
- **Specialist agent instructions** — `TicketAgentFactory` and `KBAgentFactory` strengthened with explicit "EXECUTE IMMEDIATELY" directives and richer multi-step chaining examples; `DiagnosticAgentFactory` clarifies team-wide impact detection.
- **Orchestrator instructions** — added explicit "only route to `diagnostic_agent` ONCE per conversation" guard to prevent repeated attachment analysis loops when `## Attached Document` stays in conversation history across turns.
- **AgentHost README** — corrected v2 model section to align with `docs/model-compatibility.md`; `gpt-5.2-chat` is documented as not compatible with render-action cards.

### Fixed

- **V2 attachment routing loop** — orchestrator was re-routing to `diagnostic_agent` on every turn because `## Attached Document` persists in conversation history. Added explicit once-per-conversation guard in orchestrator instructions.

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
- **Frontend responsive shell polish** — desktop-first layout now adapts across tablet and mobile widths; Settings page cards stack cleanly, route header wraps safely, and render cards no longer depend on fixed desktop widths.
- **CopilotKit controls preference** — Settings page now lets users hide or show the CopilotKit developer controls, improving the normal mobile experience.
- **Streaming chat auto-scroll** — the chat follows the assistant while streaming unless the user intentionally scrolls upward.
- **Message avatars** — assistant and user chat bubbles now show lightweight `AI` and `You` badges for easier scanning.

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

## [1.3.0] — 2026-03-20

**Auth, Persistence, and Memory**

- Added Microsoft Entra sign-in on the frontend via NextAuth and bearer-token validation in AgentHost
- Persisted tickets in Azure Cosmos DB instead of the earlier transient store
- Added `search_kb_articles` alongside KB indexing flows and aligned render-action guidance for ticket, incident, and KB cards
- Added long-term Redis-backed profile memory plus simple remembered user preferences
- Added turn-level telemetry and repeated-tool visibility to help diagnose multi-step workflow drift in Azure

---

## [1.2.0] — 2026-03-17

**Response Token Usage Stats**

- **`UsageCapturingChatClient`** (AgentHost) — new `DelegatingChatClient` that intercepts each streaming response, aggregates token usage from the final chunk, and writes `{promptTokens, completionTokens}` to Redis under `usage:{threadId}:latest`
- **`GET /agent/usage?threadId=`** (AgentHost) — new lightweight endpoint that reads the usage key from Redis and returns JSON; returns `404` if the key has not yet been written
- **`/api/copilotkit/usage/route.ts`** (Frontend) — Next.js proxy that forwards to the AgentHost usage endpoint
- **Response stats chip** (Frontend) — after each assistant response, `HelpdeskChat` fetches token usage and injects a `⏱ Xs · 📥 N in / 📤 M out` chip inline with the message's copy/thumbs action buttons

---

## [1.1.0] — 2026-03-13

**Refactoring & Upgrades**

- **Package upgrades:** `ModelContextProtocol.AspNetCore` 1.0.0 → 1.1.0; HealthChecks preview.1 → preview.2
- **C# refactor:** Extracted `ServiceStatus.cs`; de-duplicated helpers; replaced magic numbers with named constants; cleaned XML doc comments; removed `KbSearchResult` duplicate
- **TypeScript refactor:** Centralised display maps into `lib/constants.ts`; removed duplicate `const` declarations; extracted `AGENT_INSTRUCTIONS` to module scope
- **Redis:** Per-session cache keys now derived from the AG-UI `threadId`; each browser tab has fully isolated chat history

---

## [1.0.0] — Initial release

**Features**

- **AI helpdesk chat** — real-time streaming via AG-UI protocol; system prompt with user context injected via `useCopilotReadable`
- **Generative UI render actions** — `show_ticket_created`, `show_incident_alert`, `show_my_tickets` render inline cards in the chat
- **RAG** — `AzureAiSearchContextProvider` injects top-K KB articles from Azure AI Search on every turn
- **File attachments** — upload `.txt`, `.pdf`, `.docx` (OCR via Azure Document Intelligence), and `.png`/`.jpg`/`.jpeg` (vision) via `POST /api/attachments`
- **Knowledge Base, My Tickets, and Settings tabs** in the frontend shell
- **MCP tools (10 total):** ticket CRUD, system status, incidents, KB indexing and search
- **Conversation summarisation** — `SummarizingChatReducer` compresses history after N messages
- **Azure infrastructure** — Bicep one-click provisioning (`infra/deploy.ps1`) for Azure OpenAI + Azure AI Search

---

## Earlier History

See `git log` for full commit history prior to this changelog.
