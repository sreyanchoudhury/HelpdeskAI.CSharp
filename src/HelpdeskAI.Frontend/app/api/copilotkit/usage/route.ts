import { NextRequest, NextResponse } from "next/server";
import { getAuthenticatedUser } from "@/lib/server-auth";

function getAgentBase(req: NextRequest): string {
  const mode = req.cookies.get("agent-mode")?.value === "v2" ? "v2" : "v1";
  const url =
    mode === "v2"
      ? (process.env.AGENT_URL_V2 ?? `${process.env.AGENT_URL ?? "http://localhost:5200/agent"}/v2`)
      : (process.env.AGENT_URL ?? "http://localhost:5200/agent");
  return url.replace(/\/agent.*$/, "");
}

export async function GET(req: NextRequest) {
  const user = await getAuthenticatedUser(req);
  if (!user.accessToken) {
    return NextResponse.json(null, { status: 401 });
  }

  const threadId = req.nextUrl.searchParams.get("threadId");
  const qs = threadId ? `?threadId=${encodeURIComponent(threadId)}` : "";

  const agentBase = getAgentBase(req);

  try {
    const res = await fetch(`${agentBase}/agent/usage${qs}`, {
      headers: { Authorization: `Bearer ${user.accessToken}` },
    });
    if (!res.ok) return NextResponse.json(null, { status: res.status });
    return NextResponse.json(await res.json());
  } catch {
    return NextResponse.json(null, { status: 502 });
  }
}
