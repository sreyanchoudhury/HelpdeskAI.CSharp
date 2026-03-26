import {
  CopilotRuntime,
  ExperimentalEmptyAdapter,
  copilotRuntimeNextJSAppRouterEndpoint,
} from "@copilotkit/runtime";
import { HttpAgent } from "@ag-ui/client";
import { NextRequest } from "next/server";
import { getAuthenticatedUser } from "@/lib/server-auth";

const agentUrlV1 = process.env.AGENT_URL ?? "http://localhost:5200/agent";
const agentUrlV2 = process.env.AGENT_URL_V2 ?? `${agentUrlV1}/v2`;
const serviceAdapter = new ExperimentalEmptyAdapter();

function getAgentMode(req: NextRequest): "v1" | "v2" {
  return req.cookies.get("agent-mode")?.value === "v2" ? "v2" : "v1";
}

async function buildHandler(req: NextRequest) {
  const user = await getAuthenticatedUser(req);
  if (!user.accessToken) {
    return new Response(JSON.stringify({ error: "Authentication required" }), {
      status: 401,
      headers: { "content-type": "application/json" },
    });
  }

  const mode = getAgentMode(req);
  const url = mode === "v2" ? agentUrlV2 : agentUrlV1;
  const agentId = mode === "v2" ? "helpdesk-v2" : "HelpdeskAgent";

  const helpdeskAgent = new HttpAgent({
    url,
    agentId,
    headers: {
      Authorization: `Bearer ${user.accessToken}`,
    },
  }) as any; // eslint-disable-line @typescript-eslint/no-explicit-any

  const runtime = new CopilotRuntime({
    agents: {
      [agentId]: helpdeskAgent,
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
