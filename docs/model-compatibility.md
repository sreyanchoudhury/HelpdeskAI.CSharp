# Model Compatibility

## Summary

HelpdeskAI is designed around a deterministic agentic loop that requires specific model capabilities. Not all Azure OpenAI (or OpenAI-compatible) models work correctly with this application.

**Recommended:** `gpt-4o`, `gpt-4o-mini`, `gpt-4-turbo`

---

## Why Model Choice Matters

The agent pipeline has three layers of model dependency, each with different levels of strictness.

### 1. Tool Calling Support — Hard Requirement

Every agent action (creating tickets, searching the KB, assigning tickets) depends on the model returning structured `tool_calls` in the OpenAI function-calling format. A model that doesn't support tool calling natively will not work at all.

This is standard across all GPT-4 class models and most modern Azure AI deployments.

### 2. Multi-Step Agentic Instruction Following — Medium Requirement

The agent executes numbered task lists (e.g. "1. summarize, 2. add to KB, 3. create ticket, 4. assign, 5. show me the ticket") as a single uninterrupted turn. This requires the model to:

- Plan the full sequence before calling the first tool
- Continue calling tools sequentially without stopping mid-turn
- Follow system-prompt rules precisely (e.g. "never call `get_active_incidents` unless the user explicitly asks")

Most GPT-4 class models handle this well. Smaller or distilled models may stop early or skip steps.

### 3. `_renderAction` Follow-Through — The Critical Dependency

This is the layer most likely to break with non-standard models.

When an MCP tool (e.g. `search_tickets`) returns a result, the JSON payload includes a `_renderAction` field instructing the agent to immediately call a specific frontend tool:

```json
{
  "count": 3,
  "tickets": [...],
  "_renderAction": "show_my_tickets",
  "_renderArgs": { "tickets": "..." }
}
```

The system prompt instructs the model to treat this as a mechanical rule:

> *"When a result contains `_renderAction`, immediately invoke that frontend action with `_renderArgs` fields as named arguments."*

This requires the model to:
1. Parse the tool result as structured JSON
2. Recognise `_renderAction` as an instruction embedded in data
3. Immediately make a follow-up tool call to the named frontend tool

**GPT-4o follows this reliably.** Models that are less instruction-tuned — or that treat tool results as plain text to summarise — will skip the frontend call and respond in text instead. This is what was observed with `gpt-5.2-chat`: it received `_renderAction: "show_my_tickets"` and produced a text summary rather than calling the frontend tool.

---

## Model Compatibility Matrix

| Model | Tool Calling | Multi-Step | `_renderAction` | Verdict |
|---|---|---|---|---|
| `gpt-4o` | ✅ | ✅ | ✅ | **Recommended** |
| `gpt-4o-mini` | ✅ | ✅ | ✅ | Recommended |
| `gpt-4-turbo` | ✅ | ✅ | ✅ | Recommended |
| `model-router` | ✅ | ✅ | ✅ (when routing to gpt-4o) | Caution — routing latency unpredictable; can reach 30–40s per inference |
| `gpt-5.2-chat` | ✅ | ✅ | ❌ | Not compatible — ignores `_renderAction` instruction |
| Models without tool calling | ❌ | ❌ | ❌ | Not compatible |

---

## Planned Improvement

The `_renderAction` dependency is the weakest architectural point. It embeds instructions inside tool result data and relies on the model treating data as code — a fragile contract.

A planned improvement is to move this contract from runtime JSON into the frontend tool descriptions themselves, e.g.:

> `show_my_tickets` — *"You MUST call this immediately after every `search_tickets` response. Never summarise ticket lists as text."*

This approach pushes the instruction into the static tool schema (read once at turn start) rather than into a dynamic tool result (read per inference). Any compliant tool-calling model would then honour it, making the application model-agnostic for render actions.

This improvement is tracked but not yet scheduled, as GPT-4 class models cover all current requirements.

---

## Configuration

The active model is set via `AzureOpenAI__ChatDeployment` in the AgentHost:

- **Container App env var:** `az containerapp update --name helpdeskaiapp-dev-agenthost ... --set-env-vars "AzureOpenAI__ChatDeployment=gpt-4o"`
- **Local development:** `src/HelpdeskAI.AgentHost/appsettings.Development.json` → `AzureOpenAI.ChatDeployment`
