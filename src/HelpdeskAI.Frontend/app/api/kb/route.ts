import { NextRequest, NextResponse } from "next/server";

const AGENT_BASE_URL = process.env.AGENT_BASE_URL ?? "http://localhost:5200";

export async function GET(req: NextRequest) {
  const { searchParams } = new URL(req.url);
  const q = searchParams.get("q");
  try {
    const url = `${AGENT_BASE_URL}/api/kb/search${q ? `?q=${encodeURIComponent(q)}` : ""}`;
    const res = await fetch(url, { cache: "no-store" });
    const data = await res.json();
    return NextResponse.json(data, { status: res.status });
  } catch {
    return NextResponse.json({ error: "AgentHost unavailable" }, { status: 502 });
  }
}
