# HelpdeskAI - Live Demo Script
**Azure Global Event Kolkata · April 18, 2026 · ~10 minutes**

---

## Demo Positioning

Opening line:

> "This is not a chatbot demo. This is a Microsoft Agents Framework demo, and HelpdeskAI is the proof point."

Architecture framing:

This demo shows what a real MAF-based multi-agent system looks like when you combine:
- Microsoft Agents Framework for agents, handoffs, skills, AG-UI hosting, and observability hooks
- MCP for tools
- OpenTelemetry + Azure Monitor for traceability
- Azure OpenAI, Document Intelligence, Redis, Cosmos DB, and AI Search as the backing services
- Azure content safety filters with graceful SSE-safe exception handling

Conference note worth calling out once:
- The demo is now aligned to the official .NET MAF v1 core package line, with AG-UI hosting still on the compatible preview companion package.

Repeat this mentally all the way through:
- MAF is the hero
- HelpdeskAI is the example
- Azure is the operating substrate

---

## Before You Walk On Stage

- [ ] App is deployed and you can sign in successfully
- [ ] Browser tabs are pre-opened:
  1. HelpdeskAI frontend, already authenticated
  2. Azure Portal -> Application Insights / Workbook for HelpdeskAI tracing
- [ ] Settings are checked once:
  - route toggle is visible
  - `v2` is available
  - live incident banner can be shown if needed
- [ ] At least one earlier conversation exists in telemetry so the workbook is not empty
- [ ] Slide 2 or the demo-intro slide is on screen before you switch to the app

---

## Demo Goal

Show 4 things in one flow:

1. MAF can host both a single-agent route and a workflow route in the same app.
2. A real support workflow can combine incidents, KB, ticketing, and attachments.
3. Skills let the agent load specialized behavior without collapsing everything into one giant prompt.
4. AG-UI streaming and tool-driven cards create an actual product experience, not a console toy.
5. OpenTelemetry + App Insights make the system traceable by thread, route, and specialist.

---

## Step 1 - Open the App (30 sec)

**Do:** Switch to the HelpdeskAI tab.

**Say:**
> "This is HelpdeskAI. I built it as a realistic support MVP to exercise Microsoft Agents Framework end to end. The UI is not the point. The point is what MAF is enabling behind the UI."

**Do:** Open `Settings`.

**Say:**
> "Right here you can see the route toggle. `v1` is the simpler agent route. `v2` is the workflow route with an orchestrator and focused specialists. Same product surface, different MAF composition underneath."

**Optional architecture line if the audience is technical early:**
> "And beyond routes, the app is also using MAF skills, context providers, and telemetry wrappers under the hood. So this is not just prompt engineering with a nicer UI."

**Do:** Select `v2`, then return to chat.

---

## Step 2 - Show the Problem Shape (45 sec)

**Say before typing:**
> "The kind of workload I want is not a single FAQ question. I want something that needs search, reasoning, knowledge reuse, and durable action."

**Do:** Send:

```text
Show all active incidents.
```

**Say while it responds:**
> "This already demonstrates two things: tool use and structured rendering. The agent is not just generating text. It is invoking a real capability and sending structured output back to the UI."

**Validate live for yourself:**
- incident card appears
- response is clean

---

## Step 3 - Main Workflow Demo (3 min)

If you have an attachment prepared, use the attachment workflow. If not, use the text-only ticket + KB flow.

### Option A - Attachment workflow (preferred)

**Do:** Upload the prepared incident screenshot or file.

**Do:** Send:

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

**Say while it runs:**
> "Now we're seeing why a framework matters. MAF is handling the agent boundary, the workflow path, the handoff model, the context surface, and the streaming contract. The business actions sit on top through MCP, and the attachment path is where Azure Document Intelligence and vision services start adding multimodal capability."

**Optional follow-up line while the turn is still running:**
> "This is also where skills matter. If the issue looks like a major incident, a security issue, or an escalation case, the agent can load that behavior progressively instead of hardcoding everything into one prompt."

**Point out while it completes:**
- KB card
- ticket card
- structured artifacts instead of plain text only

**Say:**
> "The visible result is HelpdeskAI. The important part is the MAF architecture under it: orchestration, tool use, streaming, and traceability."

### Option B - Text-only fallback

**Do:** Send:

```text
Create a ticket for Outlook crashing when opening large attachments, priority high, provide a likely resolution, add it to the knowledge base if needed, and show me both the KB article and the ticket.
```

---

## Step 4 - Show Retry Safety (60 sec)

**Say:**
> "One thing that matters in production is retry safety. If a workflow is retried after a partial response, you do not want duplicate tickets or duplicate KB writes."

**Do:** Send:

```text
Retry that workflow and continue from where you left off.
```

**Say while it runs:**
> "This is where product behavior and architecture meet. The system can recover without replaying every side effect."

**Call out if it behaves correctly:**
- same ticket reused
- KB reused or refreshed, not duplicated

---

## Step 5 - Contrast v1 and v2 Briefly (60 sec)

**Do:** Go to `Settings`, switch to `v1`, return to chat.

**Say:**
> "The useful thing about this MVP is that it lets me show two MAF-backed shapes in one app. `v1` is the simpler single-agent route. `v2` is the workflow route. The user experience stays familiar, but the orchestration model changes."

**Do:** Send:

```text
What can you help me with in this app?
```

**Say:**
> "That contrast is important. MAF is not just one agent pattern. It lets you evolve from simpler agent handling to orchestration-heavy workflows without rewriting the whole product."

**Optional skills line here:**
> "And that evolution is not only about routing. It also includes things like skills, memory, tools, and observability staying consistent across both shapes."

---

## Step 6 - Show Observability (2 min)

**Do:** Switch to the Azure workbook / App Insights view.

**Say:**
> "This is the part I care about most for production-readiness. If I cannot trace agent routing, tool usage, latency, and thread flow, I do not really have a system I can operate."

**Point to the relevant visuals:**
- request volume / route split if visible
- conversation trace by `thread.id`
- agent routing / specialist execution
- token or latency panels if available

**Say:**
> "Every turn carries a thread identity. Every routed step is observable. This is not logging sprinkled around the edges. OpenTelemetry is part of the architecture, and MAF gives us clean seams to attach that instrumentation."

**If the workflow route was used:**
> "In the workflow route, this is where I can see which specialists were invoked and how the conversation progressed across the handoff chain."

---

## Step 7 - Content Safety (30 sec)

**Do:** Stay on the App Insights / workbook view or switch to the content safety slide.

**Say:**
> "The last thing worth calling out — because Azure mandated it on my models — is content safety in production. Azure OpenAI content filters are active on these deployments. If a request is blocked, we do not want the SSE stream to abruptly terminate with an unhandled exception and leave the frontend hanging. The system catches those before they propagate, clears the conversation history so a poisoned thread does not keep re-triggering the filter on every subsequent turn, and returns a user-readable explanation. The event is also logged with the thread ID for App Insights investigation."

**Note:** No live trigger needed — this is a callout moment, not an interactive step. Point to the content safety slide or the code snippet if the audience is technical.

---

## Step 7 - Close the Demo (30 sec)

**Do:** Return to slides, ideally Slide 14.

**Say:**
> "So the key point is not that I built an IT helpdesk demo. The key point is that Microsoft Agents Framework, plus MCP, AG-UI, and OpenTelemetry, gives you a path from prototype behavior to something you can reason about and operate on Azure."

---

## Fallback Plan

If the live app misbehaves:

1. Show the already-open workbook first.
2. Explain the architecture from the traces backward.
3. Then return to the app and use a simpler prompt:

```text
Show all active incidents.
```

4. If needed, use screenshots from an earlier successful run.

The fallback story is still strong if you can show:
- structured cards
- route contrast (`v1` vs `v2`)
- thread-level observability

---

## Timing Guide

| Segment | Target |
|---|---|
| Slides 1-4 | 6 min |
| Slides 5-13 | 10 min |
| Live demo | 10 min |
| Slides 14-15 + Q&A | 4 min |
| **Total** | **30 min** |

---

## Common Q&A

**Q: What is MAF doing here that I could not do manually?**
> You can absolutely hand-build this, but then you own every seam yourself: agent hosting, orchestration, routing, streaming, context, telemetry integration, and evolution from one route to multiple routes. MAF gives those seams structure.

**Q: What else besides MAF is important in this architecture?**
> MCP for tools, AG-UI for streaming, OpenTelemetry for traceability, and Azure services for the operational substrate. MAF is the lead framework, but the surrounding protocols matter a lot.

**Q: Are you using skills, or only prompts and tools?**
> We are using skills today through `FileAgentSkillsProvider`. They are loaded progressively and help shape behavior for cases like escalation, major incidents, security-sensitive requests, VIP handling, and frustrated users.

**Q: Is this production-ready?**
> It is an MVP designed to be production-shaped. It already includes auth, memory, ticket persistence, knowledge indexing, observability, responsive UX, and workflow routing. A real production rollout would still need deeper hardening, governance, and organizational readiness.

**Q: Why keep both v1 and v2?**
> Because that contrast is educational. It shows how MAF lets you grow from a simpler agent setup to a workflow-driven design without throwing away the product surface.

**Q: Where is the business data stored?**
> Tickets are persisted in Cosmos DB. KB content is stored in Azure AI Search. History and memory surfaces are Redis-backed. That separation is important and deliberate.

**Q: Where does Document Intelligence fit?**
> In the attachment pipeline. It extracts text from PDF and DOCX so the agent can reason over uploaded documents as part of the workflow. Images flow through the vision path.

---

## No-Miss Talking Points

If you get interrupted, rushed, or pulled into questions, make sure these points land somewhere:

- MAF is the hero. HelpdeskAI is the proof point.
- `v1` and `v2` show two different MAF compositions in the same app.
- MCP gives the tool contract.
- AG-UI gives the streaming contract.
- OpenTelemetry gives the traceability story.
- Skills are in use today, not just future-looking.
- Redis is history and long-term memory.
- Cosmos is ticket persistence, not memory.
- AI Search is KB persistence and retrieval.
- Document Intelligence is part of the multimodal attachment path.

---

## Speaker Reminders

- Do not over-explain the UI. Use the UI to reveal the architecture.
- Say "MAF" often enough that the audience remembers the framework, not just the app.
- When showing HelpdeskAI behavior, always translate it back to the MAF primitive that enabled it.
- If time gets tight, prioritize:
  1. route contrast
  2. one workflow
  3. observability
- If the live workflow is slow, narrate the architecture while it streams instead of waiting silently.
