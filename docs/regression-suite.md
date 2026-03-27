# HelpdeskAI Regression Suite

This suite is the baseline manual validation set for both agent routes:

- `v1` - `/agent`
- `v2` - `/agent/v2`

Run the suite in Azure after each non-trivial change. Unless a scenario explicitly calls for cross-browser validation, execute it once in a clean session on a single browser first.

## Route Matrix

| Route | Mode | Model | Expected Stability |
|------|------|-------|--------------------|
| `v1` | Single-agent | `gpt-4o` | Primary route - should remain broadly reliable |
| `v2` | Multi-agent workflow | `gpt-5.2-chat` | Experimental route - may still need occasional follow-up nudges on long chains |

## Core Regression Flow

Execute the following scenarios on both routes.

### 1. Auth Bootstrap and Sign-Out

1. Open the frontend in an incognito/private session.
2. Confirm you are redirected to Microsoft Entra sign-in or the short redirect screen.
3. Sign in and confirm the chat shell loads without a visible `401`.
4. Sign out.
5. Hit the app URL directly again.

Expected:

- no stale signed-in shell
- no `Runtime info request failed with status 401`
- sign-in and sign-out loop remains clean

### 2. Basic Conversational Health

Prompt:

```text
What can you help me with in this app?
```

Expected:

- normal assistant response
- no card spam
- no hung turn
- runtime info and token stats still appear

### 3. Ticket Creation and Rendering

Prompt:

```text
Create a ticket for Outlook crashing when opening large attachments, priority high, and show me the ticket.
```

Expected:

- exactly one ticket is created
- ticket card renders once
- no duplicate cards
- no duplicate ticket IDs

Known watch item:

- `v1` has shown a sporadic `show_ticket_details` parsing issue:
  `Tool arguments for show_ticket_details parsed to non-object (object)`

### 4. Ticket Retrieval

Prompt:

```text
Show me my open tickets.
```

Expected:

- ticket list renders correctly
- current signed-in user identity is respected
- no duplicate render blocks

### 5. Active Incidents

Prompt:

```text
Show all active incidents.
```

Expected:

- incident card/list renders once
- no repeated `get_active_incidents` loop
- no runaway token growth

Watch closely for:

- repeated incident cards
- large token spikes
- visibly stalled turns

### 6. Team Impact

Prompt:

```text
What issues are currently affecting the Engineering team?
```

Expected:

- relevant response
- correct team-impact rendering
- no repeated incident polling

### 7. KB Search

Prompt:

```text
Search the knowledge base for VPN certificate issues and show me the matching article.
```

Expected:

- KB result appears
- KB card renders once
- no duplicate indexing or duplicate search cards

### 8. Attachment Workflow

Upload an incident screenshot or document, then prompt:

```text
What is this incident about? Once you've analyzed it, do the following in order:
1. summarize the incident
2. provide a resolution
3. add to kb, if not already present
4. show the kb, either new or existing
5. create a ticket
6. assign it to me
7. show me the ticket
8. re-summarize all actions
```

Expected:

- workflow completes end-to-end
- no duplicate KB entries
- no duplicate tickets
- no repeated incident cards
- ticket card renders

Known watch items:

- `v2` may still need a follow-up nudge to finish long chains in one shot
- token usage is still higher than desired on both routes

### 9. Long-Term Memory

Session 1 prompt:

```text
Remember that I prefer concise answers and Azure-first examples.
```

Fresh session prompt:

```text
What do you remember about my preferences?
```

Expected:

- preference memory survives across sessions
- not limited to just name/email identity recall

### 10. Cross-Browser Sanity

Run one ticket flow and one attachment workflow in both Chrome and Edge.

Expected:

- no browser-specific `401`
- no major functional divergence

Capture:

- whether one browser loops more often
- whether token usage is materially higher in one browser
- whether cards render differently

## Stress Scenarios

These are useful, but they are not the primary pass/fail gate for every change.

### Parallel Browser Workflow Stress

Run the attachment workflow in Chrome and Edge at the same time.

Current expectation:

- useful for detecting orchestration drift
- not yet considered a fully stable production-grade scenario

Current known risks:

- duplicate cards
- duplicate side effects
- repeated `get_active_incidents`
- token blow-up and stalled turns

## Current Known Issues

- `v1`: sporadic `show_ticket_details` argument parsing failure on render path
- `v2`: complex workflows may not always complete in one uninterrupted turn
- both routes: token usage remains higher than desired on longer workflows
- parallel multi-browser stress runs can still surface duplicate rendering or duplicate side effects

## Recommended Recording Format

For each route, record:

- pass/fail per scenario
- prompt used
- whether a card rendered
- whether duplicate cards appeared
- whether duplicate side effects occurred
- visible token behavior
- browser used

## Cleanup Between Test Cycles

If regression runs have polluted Cosmos DB tickets or Azure AI Search with agent-created records, reset the non-seed artifacts before the next cycle:

```powershell
cd infra
.\cleanup-demo-data.ps1 `
  -SearchEndpoint "https://<search>.search.windows.net" `
  -SearchAdminKey "<search-admin-key>" `
  -CosmosEndpoint "https://<cosmos>.documents.azure.com:443/" `
  -CosmosPrimaryKey "<cosmos-primary-key>"
```

By default, the cleanup script:

- preserves seeded KB articles in `infra/seed-data.json`
- removes agent-indexed KB documents such as `KB-up-*`
- preserves seeded ticket documents with `seq <= 1013`
- removes later demo-created tickets
