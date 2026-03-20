import { NextRequest, NextResponse } from "next/server";
import { getAuthenticatedUser } from "@/lib/server-auth";

const AGENT_BASE_URL = process.env.AGENT_BASE_URL ?? "http://localhost:5200";

export async function GET(req: NextRequest, context: { params: Promise<{ blobName: string[] }> }) {
  const user = await getAuthenticatedUser(req);
  if (!user.accessToken) {
    return NextResponse.json({ error: "Authentication required" }, { status: 401 });
  }

  const { blobName } = await context.params;
  const target = `${AGENT_BASE_URL}/api/attachments/${blobName.map(encodeURIComponent).join("/")}`;

  try {
    const res = await fetch(target, {
      headers: { Authorization: `Bearer ${user.accessToken}` },
    });

    if (!res.ok || !res.body) {
      return new NextResponse(await res.text(), { status: res.status });
    }

    return new NextResponse(res.body, {
      status: res.status,
      headers: {
        "content-type": res.headers.get("content-type") ?? "application/octet-stream",
        "content-disposition": res.headers.get("content-disposition") ?? "attachment",
      },
    });
  } catch {
    return NextResponse.json({ error: "AgentHost unavailable" }, { status: 502 });
  }
}
