# Model Compatibility

## Current Standard

HelpdeskAI is now standardized on a two-model baseline:

- `v1` single-agent route: `gpt-5.3-chat`
- `v2` multi-agent workflow route: `gpt-5.2-chat`

This is the pairing currently used across app defaults, deployment defaults, demo collateral, and the Azure environment.

---

## Why This Repo Is Pinned

HelpdeskAI depends on three things from the model layer:

1. Native OpenAI-compatible tool calling
2. Reliable multi-step instruction following across long support workflows
3. Stable behavior when AG-UI streaming, MCP tool results, and workflow handoffs are combined in the same turn

The codebase now assumes the `gpt-5.3-chat` / `gpt-5.2-chat` pairing unless explicitly overridden.

---

## Supported Baseline Matrix

| Route | Model | Current role | Operational note |
|---|---|---|---|
| `v1` | `gpt-5.3-chat` | Primary single-agent route | Default production baseline |
| `v2` | `gpt-5.2-chat` | Multi-agent handoff workflow | Validate with the regression suite after any workflow or frontend streaming change |

Other models are no longer part of the documented baseline for this repo. If you change either deployment, treat that as a regression event and re-run the suite in [docs/regression-suite.md](docs/regression-suite.md).

---

## Known Caveats

- `v2` is still the higher-risk route because it combines orchestration, handoffs, frontend tool forwarding, and AG-UI streaming.
- Content-safety failures now clear thread-scoped transient state and force a fresh frontend chat instance, so recovery after a blocked turn should be validated as part of any release check.
- Attachment flows should always be re-tested after workflow or transport changes because they depend on thread continuity and one-shot Redis staging.

---

## Configuration

The active model defaults are configured through AgentHost settings:

- `AzureOpenAI.ChatDeployment` → `gpt-5.3-chat`
- `AzureOpenAI.ChatDeploymentV2` → `gpt-5.2-chat`

In Azure Container Apps, the same values are passed through `AzureOpenAI__ChatDeployment` and `AzureOpenAI__ChatDeploymentV2`.
