import { NextResponse } from "next/server";

// MCP_URL: internal hostname within the Container Apps environment.
// AGENT_BASE_URL: external FQDN of AgentHost (set by Bicep).
const MCP_URL        = process.env.MCP_URL        ?? "http://helpdeskaiapp-dev-mcpserver";
const AGENT_BASE_URL = process.env.AGENT_BASE_URL ?? "http://localhost:5200";

async function ping(url: string): Promise<"ok" | "down"> {
  try {
    const res = await fetch(url, { cache: "no-store", signal: AbortSignal.timeout(5000) });
    return res.ok ? "ok" : "down";
  } catch {
    return "down";
  }
}

export async function GET() {
  const [mcp, agent] = await Promise.all([
    // McpServer /healthz has no external deps so always returns 200 when running.
    ping(`${MCP_URL}/healthz`),
    // /agent/info always returns 200 when the app is up (avoids Redis health check noise).
    ping(`${AGENT_BASE_URL}/agent/info`),
  ]);
  return NextResponse.json({ mcp, agent, checkedAt: new Date().toISOString() });
}

