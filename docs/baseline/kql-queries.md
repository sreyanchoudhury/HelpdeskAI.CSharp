# Phase 1d — Performance Baseline KQL Queries

Run these in **App Insights → Logs** after Phase 1a is deployed and ≥ 20 agent
turns have been recorded. Fill the results into `metrics-baseline.json`.

> Full query reference + navigation guide: see `infra/monitoring.md`.

---

## 1. Agent Turn Latency

```kql
requests
| where name == "POST /agent"
| summarize
    p50 = percentile(duration, 50),
    p95 = percentile(duration, 95),
    p99 = percentile(duration, 99)
```

→ Fill into `agentTurnLatencyMs` (milliseconds).
Use `name == "POST /agent"` — not `name has "/agent"` which includes the
fast `/agent/usage` health pings and skews p50 to near-zero.

---

## 2. Token Counts Per Turn

```kql
traces
| where message has "Token usage"
| extend
    prompt     = toint(customDimensions.PromptTokens),
    completion = toint(customDimensions.CompletionTokens)
| summarize
    avgPromptTokens     = avg(prompt),
    p95PromptTokens     = percentile(prompt, 95),
    avgCompletionTokens = avg(completion),
    p95CompletionTokens = percentile(completion, 95)
```

→ Fill into `tokenUsagePerTurn`.

---

## 3. Per-Tool Latency

```kql
traces
| where message has "Tool call"
| where isnotempty(tostring(customDimensions.ToolName))
| summarize
    p50 = percentile(toint(customDimensions.DurationMs), 50),
    p95 = percentile(toint(customDimensions.DurationMs), 95)
  by toolName = tostring(customDimensions.ToolName)
| order by p95 desc
```

→ Fill into `perToolLatencyMs` (one entry per tool name).
Note: `customDimensions.ToolName` (capital T, capital N) — emitted as a
message-template parameter by `RetryingMcpTool` after the Phase 1a fix.

---

## Regression Check (Phase 2+)

Before merging any Phase 2+ feature, replace `baseline_p95` with the value
from `metrics-baseline.json` and run:

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

**Token regression check:**
```kql
let baseline_avg_prompt = 5866.0;  // ← update from metrics-baseline.json
traces
| where timestamp > ago(1h)
| where message has "Token usage"
| extend prompt = toint(customDimensions.PromptTokens)
| summarize current_avg = avg(prompt)
| extend
    delta_tokens = round(current_avg - baseline_avg_prompt, 0),
    alert        = iff(current_avg - baseline_avg_prompt > 200,
                       "TOKEN REGRESSION — mitigation required before merge",
                       "OK")
| project current_avg, baseline_avg_prompt, delta_tokens, alert
```
