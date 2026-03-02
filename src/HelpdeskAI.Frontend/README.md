# HelpdeskAI — Frontend

A **React 19 + TypeScript** single-page application for the IT helpdesk AI agent. Built with **Next.js** (App Router) and powered by **CopilotKit** + AG-UI.

---

## What It Does

- **Real-time chat UI** — streams responses from the AI agent as they're generated
- **Tool call rendering** — displays ticket creation, status updates, KB searches with results
- **Session management** — persists conversation threads across browser sessions
- **Responsive design** — mobile-friendly, keyboard accessible

---

## Quick Start

### Prerequisites

- **Node.js 22 LTS** — https://nodejs.org
- **Agent Host running** — `cd ../HelpdeskAI.AgentHost && dotnet run` (port 5200)

### Start Dev Server

```bash
npm install
npm run dev
# → http://localhost:3000
```

### Build for Production

```bash
npm run build
# Output: .next/ + optimized bundle
```

The frontend and backend are independent. Run the Next.js dev server separately (`npm run dev` on port 3000) or deploy it independently.

---

## Project Structure

```
app/
├── layout.tsx                    # Root layout (CopilotKit provider setup)
├── page.tsx                     # Main chat page
├── globals.css                  # Global styles
├── next.config.ts               # Next.js configuration
├── api/
│   └── copilotkit/
│       └── route.ts             # CopilotKit Runtime runtime endpoint
components/
├── HelpdeskChat.tsx             # Main chat UI component
├── HelpdeskActions.tsx          # Action buttons, quick prompts
```

---

## Architecture

```
Browser
   │
   ├─ Next.js App Router (app/)
   │   │
   │   ├─ layout.tsx — CopilotKit Provider
   │   │   │
   │   └─ page.tsx — HelpdeskChat component
   │        └─ useCopilotChatHeadless_c() — AG-UI state
   │            └─ Message streaming + tool rendering
   │
   └─ Next.js API Route (app/api/copilotkit/route.ts)
       └─ CopilotKit Runtime endpoint
```

---

## Key Components

### `app/layout.tsx`

Root layout that wraps the entire app:
- CopilotKit Provider setup
- Global metadata and styles setup

### `app/page.tsx`

Main chat page component:
- Imports `HelpdeskChat` component
- Displays the chat UI

### `components/HelpdeskChat.tsx`

Core chat component integrating:
- `useCopilotChatHeadless_c()` — CopilotKit AG-UI state hook
- Real-time message streaming
- Tool call rendering
- **Streaming cursor** — animated block while receiving response
- Tool call badges embedded when tools are invoked

### `ToolCallBadge.tsx`

Collapsible badge showing:
- Tool name (e.g., `search_tickets`, `get_system_status`)
- Arguments (pretty-printed JSON)
- Result / status (loading ⟳, success ✓, error ✗)

---

## Hooks

### `useCopilotChatHeadless_c()`

CopilotKit AG-UI state hook:

```typescript
const {
  messages,         // Message[] from CopilotKit state
  isLoading,        // boolean
  input,            // user input text
  send,             // (text: string) => void
} = useCopilotChatHeadless_c();
```

---

## Configuration

### Environment Variables

Set in `.env.local`:

| Variable | Default | Purpose |
|----------|---------|----------|
| `AGENT_URL` | `http://localhost:5200/agent` | Backend AG-UI endpoint |

For production, update `next.config.ts`:
```typescript
env: {
  AGENT_URL: process.env.AGENT_URL ?? "https://api.helpdeskai.example.com/agent",
}
```

---

## Building & Deployment

### Development

```bash
npm run dev
# Hot reload on file changes, with Turbopack
# http://localhost:3000
```

### Production Build

```bash
npm run build
npm start
# Optimized production server on port 3000
```

Or let the backend serve it:
- Copy `.next` output to backend static folder
- Configure backend to serve Next.js as static content

---

## Styling

**CSS Modules or plain CSS** via `app/globals.css`:

```css
/* Global base styles */
body { /* ... */ }
a { /* ... */ }
```

Key styles:
- **Chat bubbles** — see component files for CSS module imports
- **Responsive** — mobile-first design via media queries

---

## Dependencies

| Package | Purpose |
|-----------|----------|
| `react` | UI framework |
| `next` | Framework (app router, SSR, static gen) |
| `@copilotkit/react-core` | CopilotKit provider and hooks |
| `@copilotkit/react-ui` | CopilotKit UI components |
| `@copilotkit/runtime` | CopilotKit runtime integration |
| `@ag-ui/client` | HttpAgent (AG-UI protocol) |
| `@ag-ui/core` | AG-UI types and utilities |
| `typescript` | Type checking |

---

## Troubleshooting

### "Cannot find module '@copilotkit/react-core'"

**Fix:**
```bash
rm -r node_modules .next
npm cache clean --force
npm install
```

### "Next.js dev server won't start"

**Fix:** Ensure Agent Host is running on port 5200:
```bash
cd ../HelpdeskAI.AgentHost
dotnet run
```

### "Chat responses not streaming"

**Symptom:** Loading spinner spins forever

**Fix:**
1. Open DevTools → Network → filter `agent`
2. Check POST request to agent endpoint
3. If 502, Agent Host isn't running
4. If request succeeds, check Console for JS errors

---

## Learn More

- **Next.js Docs:** https://nextjs.org/docs
- **React Docs:** https://react.dev
- **CopilotKit:** https://docs.copilotkit.ai
- **AG-UI Protocol:** https://aka.ms/ag-ui
