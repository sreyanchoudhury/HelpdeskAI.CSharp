import { NextRequest, NextResponse } from "next/server";
import { getAuthenticatedUser } from "@/lib/server-auth";

const AGENT_BASE_URL = process.env.AGENT_BASE_URL ?? "http://localhost:5200";

export async function GET(req: NextRequest) {
  const { searchParams } = new URL(req.url);
  const user = await getAuthenticatedUser(req);
  if (!user.email || !user.accessToken) {
    return NextResponse.json({ error: "Authentication required" }, { status: 401 });
  }

  if (!searchParams.get("requestedBy")) {
    searchParams.set("requestedBy", user.email);
  }

  const qs = searchParams.toString();
  try {
    const res = await fetch(`${AGENT_BASE_URL}/api/tickets${qs ? `?${qs}` : ""}`, {
      cache: "no-store",
      headers: { Authorization: `Bearer ${user.accessToken}` },
    });
    const data = await res.json();
    return NextResponse.json(data, { status: res.status });
  } catch {
    return NextResponse.json({ error: "McpServer unavailable" }, { status: 502 });
  }
}
