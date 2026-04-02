import { NextRequest, NextResponse } from "next/server";
import { getAuthenticatedUser } from "@/lib/server-auth";

const agentUrl = process.env.AGENT_URL ?? "http://localhost:5200/agent";
const evalApiKey = process.env.EVAL_API_KEY ?? "";

function evalHeaders() {
  return {
    "Content-Type": "application/json",
    "X-Eval-Key": evalApiKey,
  };
}

/**
 * GET /api/eval-results
 *   ?run={executionName}  → GET /agent/eval/results/{executionName}  (scenario details)
 *   (no ?run)             → GET /agent/eval/results                  (execution summaries)
 *
 * POST /api/eval-results
 *   → POST /agent/eval/run  (trigger new background eval run)
 */

async function guard(req: NextRequest): Promise<Response | null> {
  const user = await getAuthenticatedUser(req);
  if (!user.accessToken) {
    return NextResponse.json({ error: "Authentication required" }, { status: 401 });
  }
  return null;
}

export async function GET(req: NextRequest): Promise<Response> {
  const authErr = await guard(req);
  if (authErr) return authErr;

  const run = req.nextUrl.searchParams.get("run");
  const upstream = run
    ? `${agentUrl}/eval/results/${encodeURIComponent(run)}`
    : `${agentUrl}/eval/results`;

  try {
    const res = await fetch(upstream, { headers: evalHeaders(), cache: "no-store" });
    const body = await res.text();
    return new NextResponse(body, {
      status: res.status,
      headers: { "Content-Type": "application/json" },
    });
  } catch (err) {
    return NextResponse.json({ error: String(err) }, { status: 502 });
  }
}

export async function POST(req: NextRequest): Promise<Response> {
  const authErr = await guard(req);
  if (authErr) return authErr;

  try {
    const res = await fetch(`${agentUrl}/eval/run`, {
      method: "POST",
      headers: evalHeaders(),
      cache: "no-store",
    });
    const body = await res.text();
    return new NextResponse(body, {
      status: res.status,
      headers: { "Content-Type": "application/json" },
    });
  } catch (err) {
    return NextResponse.json({ error: String(err) }, { status: 502 });
  }
}
