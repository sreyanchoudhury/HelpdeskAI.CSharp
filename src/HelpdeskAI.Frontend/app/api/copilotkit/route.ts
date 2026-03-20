import {
  CopilotRuntime,
  ExperimentalEmptyAdapter,
  copilotRuntimeNextJSAppRouterEndpoint,
} from "@copilotkit/runtime";
import { HttpAgent } from "@ag-ui/client";
import { NextRequest } from "next/server";
import { getAuthenticatedUser } from "@/lib/server-auth";

const agentUrl = process.env.AGENT_URL ?? "http://localhost:5200/agent";
const serviceAdapter = new ExperimentalEmptyAdapter();

async function buildHandler(req: NextRequest) {
  const user = await getAuthenticatedUser(req);
  if (!user.accessToken) {
    return new Response(JSON.stringify({ error: "Authentication required" }), {
      status: 401,
      headers: { "content-type": "application/json" },
    });
  }

  const helpdeskAgent = new HttpAgent({
    url: agentUrl,
    agentId: "HelpdeskAgent",
    headers: {
      Authorization: `Bearer ${user.accessToken}`,
    },
  }) as any; // eslint-disable-line @typescript-eslint/no-explicit-any

  const runtime = new CopilotRuntime({
    agents: {
      HelpdeskAgent: helpdeskAgent,
    },
  });

  const { handleRequest } = copilotRuntimeNextJSAppRouterEndpoint({
    runtime,
    serviceAdapter,
    endpoint: "/api/copilotkit",
  });
  return handleRequest(req);
}

export const GET  = (req: NextRequest) => buildHandler(req);
export const POST = (req: NextRequest) => buildHandler(req);
