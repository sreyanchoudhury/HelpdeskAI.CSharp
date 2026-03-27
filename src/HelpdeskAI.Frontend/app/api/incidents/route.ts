import { NextResponse } from "next/server";
import { getAuthenticatedUser } from "@/lib/server-auth";

const AGENT_BASE_URL = process.env.AGENT_BASE_URL ?? "http://localhost:5200";

export async function GET() {
  const user = await getAuthenticatedUser();
  if (!user.accessToken) {
    return NextResponse.json({ error: "Authentication required" }, { status: 401 });
  }

  try {
    const res = await fetch(`${AGENT_BASE_URL}/api/incidents/active`, {
      cache: "no-store",
      headers: { Authorization: `Bearer ${user.accessToken}` },
    });
    const data = await res.json();
    return NextResponse.json(data, { status: res.status });
  } catch {
    return NextResponse.json({ error: "Incident feed unavailable" }, { status: 502 });
  }
}
