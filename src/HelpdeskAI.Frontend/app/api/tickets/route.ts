import { NextRequest, NextResponse } from "next/server";

const MCP_URL = process.env.MCP_URL ?? "http://127.0.0.1:5100";

export async function GET(req: NextRequest) {
  const { searchParams } = new URL(req.url);
  const qs = searchParams.toString();
  try {
    const res = await fetch(`${MCP_URL}/tickets${qs ? `?${qs}` : ""}`, {
      cache: "no-store",
    });
    const data = await res.json();
    return NextResponse.json(data, { status: res.status });
  } catch {
    return NextResponse.json({ error: "McpServer unavailable" }, { status: 502 });
  }
}
