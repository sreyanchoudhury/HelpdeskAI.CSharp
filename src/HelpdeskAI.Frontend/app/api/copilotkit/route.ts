import {
  CopilotRuntime,
  ExperimentalEmptyAdapter,
  copilotRuntimeNextJSAppRouterEndpoint,
} from "@copilotkit/runtime";
import { HttpAgent } from "@ag-ui/client";
import { NextRequest } from "next/server";

const agentUrl = process.env.AGENT_URL ?? "http://localhost:5200/agent";

// HttpAgent implements the AG-UI protocol; the CopilotRuntime agents map expects a
// CopilotKitAgent shape, but the runtime accepts any object with the AG-UI call interface.
const helpdeskAgent = new HttpAgent({ url: agentUrl, agentId: "HelpdeskAgent" }) as any; // eslint-disable-line @typescript-eslint/no-explicit-any

const serviceAdapter = new ExperimentalEmptyAdapter();

const runtime = new CopilotRuntime({
  agents: {
    HelpdeskAgent: helpdeskAgent,
  },
});

async function buildHandler(req: NextRequest) {
  const { handleRequest } = copilotRuntimeNextJSAppRouterEndpoint({
    runtime,
    serviceAdapter,
    endpoint: "/api/copilotkit",
  });
  return handleRequest(req);
}

export const GET  = (req: NextRequest) => buildHandler(req);
export const POST = (req: NextRequest) => buildHandler(req);
