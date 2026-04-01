import {
  CopilotRuntime,
  ExperimentalEmptyAdapter,
  copilotRuntimeNextJSAppRouterEndpoint,
} from "@copilotkit/runtime";
import { HttpAgent } from "@ag-ui/client";
import { NextRequest } from "next/server";

// Falls back to AGENT_URL + /demo so no new env var is required in most setups.
const agentDemoUrl =
  process.env.AGENT_DEMO_URL ??
  `${process.env.AGENT_URL ?? "http://localhost:5200/agent"}/demo`;

const serviceAdapter = new ExperimentalEmptyAdapter();

async function buildHandler(req: NextRequest) {
  const helpdeskAgent = new HttpAgent({
    url: agentDemoUrl,
    agentId: "HelpdeskAgent",
    // No Authorization header — anonymous access, matches /agent/demo on AgentHost
  }) as any; // eslint-disable-line @typescript-eslint/no-explicit-any

  const runtime = new CopilotRuntime({
    agents: { HelpdeskAgent: helpdeskAgent },
  });

  const { handleRequest } = copilotRuntimeNextJSAppRouterEndpoint({
    runtime,
    serviceAdapter,
    endpoint: "/api/copilotkit/demo",
  });

  return handleRequest(req);
}

export const GET = (req: NextRequest) => buildHandler(req);
export const POST = (req: NextRequest) => buildHandler(req);
