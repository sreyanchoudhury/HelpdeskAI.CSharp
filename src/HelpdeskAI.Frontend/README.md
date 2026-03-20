# HelpdeskAI Frontend

The frontend is a Next.js App Router application that hosts the chat UI, session UX, and server-side proxy routes.

## Responsibilities

- Authenticate users with Microsoft Entra ID through NextAuth.
- Redirect unauthenticated users into the Entra sign-in flow.
- Render chat, tickets, knowledge base, settings, and attachment UI.
- Forward authenticated requests to AgentHost and other backend routes.
- Proxy attachment downloads so private blobs remain protected.

## Local Configuration

Create `src/HelpdeskAI.Frontend/.env.local`:

```env
AGENT_URL=http://localhost:5200/agent
AGENT_BASE_URL=http://localhost:5200
MCP_URL=http://localhost:5100
NEXTAUTH_URL=http://localhost:3000
NEXTAUTH_SECRET=<random-secret>
AZURE_AD_CLIENT_ID=<entra-app-client-id>
AZURE_AD_CLIENT_SECRET=<entra-app-client-secret>
AZURE_AD_TENANT_ID=<entra-tenant-id>
AZURE_AD_API_SCOPE=api://<entra-app-client-id>/access_as_user
```

Notes:

- `AGENT_URL` must include `/agent`.
- `AGENT_BASE_URL` must not include `/agent`.
- Local frontend development can point at Azure-hosted services directly.

## Main Routes

- `/`: app shell and chat experience
- `/sign-in`: branded sign-in entry page that redirects to Microsoft Entra
- `/signed-out`: signed-out landing page with sign-in recovery
- `/api/copilotkit`: AG-UI runtime proxy
- `/api/copilotkit/usage`: token usage proxy
- `/api/kb`: knowledge base proxy
- `/api/tickets`: ticket list proxy
- `/api/upload`: attachment upload proxy
- `/api/attachments/[...blobName]`: authenticated attachment download proxy
- `/api/auth/[...nextauth]`: NextAuth route handler

## Local Run

```powershell
cd src/HelpdeskAI.Frontend
npm install
npm run dev
```

Open `http://localhost:3000`.

## Azure Notes

- The frontend keeps the browser session.
- Frontend server routes resolve the current NextAuth server session and forward the current bearer token to AgentHost.
- `NEXTAUTH_URL`, `NEXTAUTH_SECRET`, and `AZURE_AD_*` values are required in Azure.
- The current deployed UX redirects signed-out users to Entra and uses a signed-out recovery page after logout.
- NextAuth now refreshes the Entra access token before expiry so authenticated proxy routes keep working across longer sessions.

## Dependencies

- `next` 16.2.0
- `next-auth` 4.24.13
- `react` 19.2.4
- `@copilotkit/react-core` 1.54.0
- `@copilotkit/react-ui` 1.54.0
- `@copilotkit/runtime` 1.54.0
- `@ag-ui/client` 0.0.47
- `@ag-ui/core` 0.0.47

## Current Caveats

- Session continuity still depends on a valid refresh token being returned by the Entra app registration and consent flow.
- Render cards still depend on the model following the declared render-action contract after tool calls.
