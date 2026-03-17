import { NextRequest, NextResponse } from "next/server";

const agentBase = (process.env.AGENT_URL ?? "http://localhost:5200/agent").replace(/\/agent$/, "");

export async function GET(req: NextRequest) {
  const threadId = req.nextUrl.searchParams.get("threadId");
  if (!threadId) return NextResponse.json(null, { status: 400 });

  try {
    const res = await fetch(`${agentBase}/agent/usage?threadId=${encodeURIComponent(threadId)}`);
    if (!res.ok) return NextResponse.json(null, { status: res.status });
    return NextResponse.json(await res.json());
  } catch {
    return NextResponse.json(null, { status: 502 });
  }
}
