# HelpdeskAI — Monitoring & Observability

All telemetry flows through **Azure Monitor OpenTelemetry** into a single
Application Insights instance per environment. No extra SDK setup is required.

---

## Where to Look

### Azure Portal Navigation

```
Azure Portal
└── Resource Groups
    └── rg-helpdeskaiapp-dev
        └── helpdeskaiapp-dev-ai-vlfb75zl  (Application Insights)
            ├── Logs          ← KQL queries (everything below)
            ├── Live Metrics  ← real-time during active testing
            ├── Performance   ← request duration drill-down, dependency map
            └── Failures      ← exceptions, failed requests, 5xx errors
```

**Direct link to Logs:**
`Azure Portal → App Insights → Logs` — paste any query from this file.

> **App Insights vs Log Analytics:** Both Logs UIs work. App Insights Logs uses
> the classic table names (`requests`, `traces`, `exceptions`) directly.
> Log Analytics uses workspace names (`AppRequests`, `AppTraces`). All queries
> here use the App Insights UI / classic names.

---

## Telemetry Sources

| Signal | Table | Emitted by | Key customDimensions |
|--------|-------|-----------|----------------------|
| HTTP requests | `requests` | ASP.NET Core + OpenTelemetry | `name`, `duration`, `success` |
| Agent turn token usage | `traces` | `UsageCapturingChatClient` | `PromptTokens`, `CompletionTokens`, `ThreadId` |
| MCP tool call audit | `traces` | `RetryingMcpTool` | `ToolName`, `Attempt`, `Outcome`, `DurationMs` |
| Redis / search errors | `traces` | Various infrastructure | `message`, `CategoryName` |
| .NET exceptions | `exceptions` | OpenTelemetry auto-instrumentation | `type`, `outerMessage` |

---

## KQL Query Reference

### LLM / Token Metrics

**Token usage over time (timechart)**
```kql
traces
| where timestamp > ago(24h)
| where message has "Token usage"
| extend
    prompt     = toint(customDimensions.PromptTokens),
    completion = toint(customDimensions.CompletionTokens)
| project timestamp, prompt, completion
| render timechart
```

**Token usage summary (avg + p95)**
```kql
traces
| where timestamp > ago(24h)
| where message has "Token usage"
| extend
    prompt     = toint(customDimensions.PromptTokens),
    completion = toint(customDimensions.CompletionTokens)
| summarize
    turns            = count(),
    avgPrompt        = avg(prompt),
    p95Prompt        = percentile(prompt, 95),
    avgCompletion    = avg(completion),
    p95Completion    = percentile(completion, 95)
```

**Token usage by thread (per conversation)**
```kql
traces
| where timestamp > ago(24h)
| where message has "Token usage"
| extend
    prompt     = toint(customDimensions.PromptTokens),
    completion = toint(customDimensions.CompletionTokens),
    thread     = tostring(customDimensions.ThreadId)
| summarize totalPrompt=sum(prompt), totalCompletion=sum(completion), turns=count() by thread
| order by totalPrompt desc
```

---

### Agent Turn Latency

**p50 / p95 / p99 latency (agent turns only)**
```kql
requests
| where timestamp > ago(24h)
| where name == "POST /agent"
| summarize
    count = count(),
    p50   = percentile(duration, 50),
    p95   = percentile(duration, 95),
    p99   = percentile(duration, 99)
```

**Latency trend over time**
```kql
requests
| where timestamp > ago(24h)
| where name == "POST /agent"
| summarize p50=percentile(duration,50), p95=percentile(duration,95) by bin(timestamp, 1h)
| render timechart
```

---

### MCP Tool Call Audit

**Tool call stats — per tool (calls, avg/p95 ms, retry rate)**
```kql
traces
| where timestamp > ago(24h)
| where message has "Tool call"
| extend
    toolName   = tostring(customDimensions.ToolName),
    outcome    = tostring(customDimensions.Outcome),
    durationMs = toint(customDimensions.DurationMs)
| summarize
    calls    = count(),
    avgMs    = avg(durationMs),
    p95Ms    = percentile(durationMs, 95),
    retries  = countif(outcome == "success_after_retry"),
    failures = countif(outcome == "failure")
  by toolName
| order by calls desc
```

**Validate ToolName is populated (post-Phase 1a fix)**
```kql
traces
| where timestamp > ago(30m)
| where message has "Tool call"
| extend
    toolName   = tostring(customDimensions.ToolName),
    outcome    = tostring(customDimensions.Outcome),
    attempt    = toint(customDimensions.Attempt),
    durationMs = toint(customDimensions.DurationMs)
| project timestamp, toolName, outcome, attempt, durationMs
| order by timestamp desc
```

**Transport retries over time (should be near zero after Phase 1a fix)**
```kql
traces
| where timestamp > ago(24h)
| where message has "Tool call"
| extend outcome = tostring(customDimensions.Outcome)
| summarize
    success       = countif(outcome == "success"),
    afterRetry    = countif(outcome == "success_after_retry"),
    failures      = countif(outcome == "failure")
  by bin(timestamp, 1h)
| render columnchart
```

---

### Error Rate

**HTTP error rate (last 15 min — mirrors the alert rule)**
```kql
requests
| where timestamp > ago(15m)
| summarize
    total  = count(),
    failed = countif(success == false)
| extend errorRate = round(todouble(failed) / todouble(total) * 100, 2)
| project total, failed, errorRate
```

**Exceptions over time**
```kql
exceptions
| where timestamp > ago(24h)
| summarize count() by type, bin(timestamp, 1h)
| render timechart
```

---

### Regression Check (Phase 2+)

Run before merging any Phase 2+ feature. Replace `baseline_p95` with the value
from `docs/baseline/metrics-baseline.json`.

```kql
let baseline_p95 = 13224.0;  // ← update from metrics-baseline.json after each phase tag
requests
| where timestamp > ago(1h)
| where name == "POST /agent"
| summarize current_p95 = percentile(duration, 95)
| extend
    delta_pct = round((current_p95 - baseline_p95) / baseline_p95 * 100, 1),
    alert     = iff((current_p95 - baseline_p95) / baseline_p95 > 0.20,
                    "REGRESSION — mitigation required before merge",
                    "OK")
| project current_p95, baseline_p95, delta_pct, alert
```

---

## Alert Rules (Phase 1c)

Three `scheduledQueryRules` deployed to App Insights — visible at:
**Azure Portal → Monitor → Alerts → Alert rules**

| Rule name | Threshold | Frequency | Severity |
|-----------|-----------|-----------|----------|
| `helpdeskaiapp-dev-alert-error-rate` | HTTP error rate > 1% (min 10 requests) over 15 min | Every 5 min | Sev 2 |
| `helpdeskaiapp-dev-alert-p95-latency` | `/agent` p95 latency > 10 s over 15 min | Every 5 min | Sev 2 |
| `helpdeskaiapp-dev-alert-redis-connectivity` | ≥ 3 Redis error traces in 5 min | Every 5 min | Sev 1 |

> Alert actions: no email/Teams action group wired yet. Alerts fire to the
> Azure Monitor Alerts feed. Add an action group (email, Teams webhook, PagerDuty)
> via the `actions` block in `infra/app-deploy/apps.bicep` when needed.

**Verify alert rules are active:**
```bash
az monitor scheduled-query list \
  --resource-group rg-helpdeskaiapp-dev \
  --query "[].{name:name, enabled:properties.enabled}" \
  -o table
```

---

## Phase 1d — Performance Baseline

Captured numbers are in `docs/baseline/metrics-baseline.json`.
KQL queries to re-measure are in `docs/baseline/kql-queries.md`.

**Regression rule (Phase 2+):**
- p95 agent turn latency increases > 20% → mitigation plan required
- avg prompt tokens per turn increases > 200 tokens → mitigation plan required
