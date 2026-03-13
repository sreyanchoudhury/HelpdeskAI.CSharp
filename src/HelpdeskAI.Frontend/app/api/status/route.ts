import { NextResponse } from "next/server";

const MCP_URL   = process.env.MCP_URL   ?? "http://127.0.0.1:5100";
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
    ping(`${MCP_URL}/healthz`),
    ping(`${AGENT_BASE_URL}/healthz`),
  ]);
  return NextResponse.json({ mcp, agent, checkedAt: new Date().toISOString() });
}

