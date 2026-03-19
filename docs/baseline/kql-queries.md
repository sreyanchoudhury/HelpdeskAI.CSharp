# Phase 1d — Performance Baseline KQL Queries

Run these in Azure Monitor → Log Analytics → Logs after Phase 1a is deployed and
≥ 20 agent turns have been recorded. Fill the results into `metrics-baseline.json`.

---

## 1. Agent Turn Latency

```kql
requests
| where name has "/agent"
| summarize
    p50 = percentile(duration, 50),
    p95 = percentile(duration, 95),
    p99 = percentile(duration, 99)
```

→ Fill into `agentTurnLatencyMs` (values are in milliseconds).

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
    avgCompletionTokens = avg(completion)
```

→ Fill into `tokenUsagePerTurn`.

---

## 3. Per-Tool Latency

```kql
traces
| where isnotempty(tostring(customDimensions.toolName))
| summarize
    p50 = percentile(todouble(customDimensions.durationMs), 50),
    p95 = percentile(todouble(customDimensions.durationMs), 95)
  by toolName = tostring(customDimensions.toolName)
| order by p95 desc
```

→ Fill into `perToolLatencyMs` (one entry per tool name).

---

## Regression Check (Phase 2+)

Before merging any Phase 2+ feature:

```kql
// Compare current p95 turn latency against baseline
let baseline_p95 = 0;  // ← fill from metrics-baseline.json
requests
| where name has "/agent"
| summarize current_p95 = percentile(duration, 95)
| extend regression_pct = (current_p95 - baseline_p95) / baseline_p95 * 100
| project current_p95, baseline_p95, regression_pct,
          alert = iff(regression_pct > 20, "⚠ REGRESSION — mitigation required", "✓ OK")
```
