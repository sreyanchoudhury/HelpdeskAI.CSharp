# HelpdeskAI — Demo Script (30 min)

> **Framing:** The helpdesk is the use-case. MAF, AG-UI, MCP, and Azure are the story.

---

## Architecture (say this, don't need a slide)

| Layer | Technology |
|---|---|
| **Frontend** | Next.js + CopilotKit — AG-UI consumer |
| **AgentHost** | ASP.NET Core · `MapAGUI()` · MAF providers · GPT-4.1 |
| **McpServer** | Separate .NET service · 10 tools over MCP Streamable HTTP |
| **Azure** | OpenAI · AI Search · Document Intelligence · Blob Storage · Redis · Container Apps |

**One-sentence pitch:**
> "A production-grade AI agent built on open standards — AG-UI for streaming, MCP for tools, Microsoft Agents Framework for orchestration, Azure for intelligence."

**Key point:** Every layer is swappable. MCP means any client can call these tools. AG-UI means any frontend can stream from this agent.

---

## Run of Show

| # | Section | Time | What it demonstrates |
|---|---|---|---|
| 1 | Opening | 3 min | Architecture overview |
| 2 | RAG & Streaming | 5 min | Azure AI Search, AG-UI streaming |
| 3 | MCP Tool Chain | 8 min | Ticket lifecycle, dynamic tool selection, render cards |
| 4 | Document Intelligence | 8 min | Upload → extract → index → retrieve |
| 5 | Under the Hood | 4 min | Framework seams — talk through without switching screens |
| 6 | Close + Q&A | 2 min | — |

---

## Talking Points by Section

### Section 2 — RAG & Streaming
> "The agent never sees the full KB. Azure AI Search embeds the query, semantically ranks articles, and injects only the top 3 as system context. No prompt stuffing, no stale data."

> "Everything you see streaming is server-sent events — the AG-UI protocol. The backend is one line: `app.MapAGUI()`. The frontend uses `HttpAgent` from `@ag-ui/client`. Completely standard contract."

### Section 3 — MCP Tool Chain
> "Each tool call is JSON-RPC over MCP Streamable HTTP to a completely separate service. AgentHost doesn't know how the tools work — it just speaks MCP. Any other client could call these same tools."

> "Dynamic tool selection: the user's message is embedded at runtime, cosine similarity ranks all 10 tools, the model only sees the top 5. No context pollution, no hallucinated calls to irrelevant tools."

> "The rich cards — ticket created, incident alert, ticket list — are CopilotKit render actions. The agent emits a structured payload, the frontend renders it. Fully decoupled."

### Section 4 — Document Intelligence
> "The document never touches the model as a raw file. Azure Document Intelligence extracts clean text, we inject it as system context, and the agent answers from it. Images go through vision content-parts — same pipeline, different modality."

> "Closing the loop: the agent can index what it just read directly into Azure AI Search via `index_kb_article`. No human in the loop. Upload → extract → index → retrieve — all in one conversation."

### Section 5 — Under the Hood
> "MAF's provider chain runs before every LLM call: AI Search injects KB context, the attachment store injects uploaded docs, dynamic tool selector filters tools. The model call is last — perfectly prepared context every turn."

> "History is Redis-persisted per thread, 30-day TTL. Long conversations auto-summarise at 40 messages via MAF's `SummarizingChatReducer` — 5 tail messages + a compressed prefix. No context window blowouts."

> "MCP sessions expire on McpServer restart. We wrap every tool in a `RetryingMcpTool` — catches Session not found, reconnects, retries once, transparent to the user. We hit this in production during this build."

### Closing
> "The helpdesk is just the skin. Swap the system prompt and the MCP tools — you have a different agent entirely. The frameworks handle everything else."

---

## Prompts

**Total: 13 prompts across 3 chains + 4 backup prompts.**

---

### Chain 1 — RAG & Streaming (Prompts 1–2)
*Start simple. Show the KB working before touching any tools.*

**P1**
```
How do I fix a VPN connection issue?
```
> Azure AI Search retrieves KB articles → answer cites numbered steps. AG-UI streams tokens live.

**P2**
```
What are the steps to reset a forgotten password?
```
> Different topic, same pattern. Establishes: every turn embeds the query and injects fresh KB context.

---

### Chain 2 — MCP Tool Chain / Ticket Lifecycle (Prompts 3–8)
*Multi-step chain. Each prompt builds on the last. Shows MCP, dynamic tool selection, render cards, and conversation memory.*

**P3**
```
My VPN isn't connecting. Can you raise a support ticket? My email is alex.johnson@contoso.com
```
> `create_ticket` → **show_ticket_created** card. First MCP call. Note the ticket ID — used in P7/P8.

**P4**
```
Are there any active incidents I should know about?
```
> `get_active_incidents` → **show_incident_alert** with severity tags (🔴🟡🟠).
> Point out: different tool selected because different query — dynamic selection working.

**P5**
```
Is the networking team affected by any of these?
```
> `check_impact_for_team` — agent knows "these" because history is persisted in Redis. Shows memory.

**P6**
```
Search for any open VPN tickets
```
> `search_tickets` → **show_my_tickets** card with status/priority badges.

**P7**
```
Add a comment to that ticket — the user has already tried restarting their machine and reinstalling the VPN client
```
> `add_ticket_comment` — "that ticket" resolved from context. Multi-turn chaining across tool calls.

**P8**
```
Mark it as resolved — fixed after updating the VPN client to v4.2
```
> `update_ticket_status` with resolution notes. Closes the full ticket lifecycle loop.

---

### Chain 3 — Document Intelligence (Prompts 9–12)
*Upload first, then chain 4 prompts. Shows Doc Intelligence → Blob Storage → Redis → AI Search.*

**[Upload]** `INC1009_Monitoring_Dashboard_Error.pdf`
> While uploading, say: "Document Intelligence is extracting text, the file is being archived to Blob Storage, and the extracted content is staged in Redis ready for the next message."

**P9**
```
What is this incident about?
```
> Agent answers from the attachment — no "attached" keyword needed. **show_attachment_preview** card with one-line summary. Extracted text was injected as system context.

**P10**
```
What was the root cause and what was the resolution?
```
> Deeper question from the same document. Still in context — one-shot staging means it was consumed but the answer comes from the injected system message.

**P11**
```
Save this as a knowledge base article so the team can refer to it later
```
> `index_kb_article` → article written to Azure AI Search. Agent does it immediately, no approval needed.

**P12**
```
What do we know about monitoring dashboard errors?
```
> Retrieves the just-indexed article via AI Search. Full loop closed: **upload → extract → index → retrieve** in one conversation thread.

---

### Backup / Q&A Prompts

**B1** — System health
```
What's the current status of the email and VPN systems?
```
> `get_system_status` with live operational data.

**B2** — Ticket detail card
```
Show me everything about ticket TKT-001
```
> `get_ticket` → **show_ticket_details** full card with all metadata.

**B3** — Assignment
```
Assign that ticket to the network operations team
```
> `assign_ticket` — shows agent tracking ticket ID across the conversation.

**B4** — KB article card
```
Find me KB articles about printer issues
```
> `search_tickets` + AI Search → **show_kb_article** and **suggest_related_articles** cards.

---

## Pre-Demo Checklist

- [ ] Frontend live and loading
- [ ] Send one test message to confirm tool index is ready ("Tool index ready: 10 tools embedded" in logs)
- [ ] `INC1009_Monitoring_Dashboard_Error.pdf` ready to drag-and-drop
- [ ] Settings tab open in a second window (shows health status of all Azure services)
- [ ] Know the ticket ID from P3 — you'll reference it in P7, P8, B2, B3
