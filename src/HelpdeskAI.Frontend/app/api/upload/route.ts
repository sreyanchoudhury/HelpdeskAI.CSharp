import { NextRequest, NextResponse } from "next/server";
import { getAuthenticatedUser } from "@/lib/server-auth";

// Derive AGENT_BASE from the active agent URL (v1 or v2 based on cookie).
function getAgentBase(req: NextRequest): string {
  const mode = req.cookies.get("agent-mode")?.value === "v2" ? "v2" : "v1";
  const url =
    mode === "v2"
      ? (process.env.AGENT_URL_V2 ?? `${process.env.AGENT_URL ?? "http://localhost:5200/agent"}/v2`)
      : (process.env.AGENT_URL ?? "http://localhost:5200/agent");
  return url.replace(/\/agent.*$/, "");
}

export async function POST(req: NextRequest) {
  try {
    return await handleUpload(req);
  } catch (err) {
    console.error("[upload] Unhandled exception:", err);
    return NextResponse.json({ error: "Internal upload error", detail: String(err) }, { status: 500 });
  }
}

async function handleUpload(req: NextRequest) {
  const user = await getAuthenticatedUser(req);
  if (!user.accessToken) {
    return NextResponse.json({ error: "Authentication required" }, { status: 401 });
  }

  const contentType = req.headers.get("content-type") ?? "";

  if (!contentType.includes("multipart/form-data")) {
    return NextResponse.json({ error: "Expected multipart/form-data" }, { status: 400 });
  }

  const formData = await req.formData();
  const file = formData.get("file");

  if (!file || !(file instanceof File)) {
    return NextResponse.json({ error: "No file provided" }, { status: 400 });
  }

  // Session ID forwarded from the frontend (X-Session-Id = CopilotKit threadId).
  // Required — without it, the attachment would be staged under a shared key and
  // could leak into another user's next agent turn.
  const sessionId = req.headers.get("X-Session-Id");
  if (!sessionId) {
    return NextResponse.json({ error: "X-Session-Id header is required" }, { status: 400 });
  }

  const AGENT_BASE = getAgentBase(req);
  console.log(`[upload] sessionId=${sessionId}, agentBase=${AGENT_BASE}, file=${file.name}, mode=${req.cookies.get("agent-mode")?.value ?? "v1"}`);

  // Forward to AgentHost
  const forwardForm = new FormData();
  forwardForm.append("file", file);

  let agentResponse: Response;
  try {
    agentResponse = await fetch(`${AGENT_BASE}/api/attachments`, {
      method: "POST",
      headers: {
        "X-Session-Id": sessionId,
        Authorization: `Bearer ${user.accessToken}`,
      },
      body: forwardForm,
    });
  } catch (err) {
    console.error("[upload] Failed to reach AgentHost:", AGENT_BASE, err);
    return NextResponse.json({ error: "AgentHost unreachable" }, { status: 502 });
  }
  console.log(`[upload] AgentHost responded: status=${agentResponse.status}`);

  // agentResponse.json() can throw if AgentHost returns HTML (e.g. dev exception page)
  let body: unknown;
  try {
    body = await agentResponse.json();
  } catch {
    const text = await agentResponse.text().catch(() => "");
    console.error("[upload] AgentHost returned non-JSON:", agentResponse.status, text.slice(0, 500));
    return NextResponse.json(
      { error: `AgentHost error ${agentResponse.status}`, detail: text.slice(0, 200) },
      { status: agentResponse.status >= 400 ? agentResponse.status : 500 }
    );
  }

  if (body && typeof body === "object" && "blobUrl" in body) {
    const value = (body as { blobUrl?: unknown }).blobUrl;
    if (typeof value === "string" && value.length > 0) {
      try {
        const blobPath = new URL(value).pathname.replace(/^\/api\/attachments\//, "");
        (body as { blobUrl?: string }).blobUrl = `/api/attachments/${blobPath}`;
      } catch {
        // Leave the original URL intact if it is not a parseable absolute URI.
      }
    }
  }

  return NextResponse.json(body, { status: agentResponse.status });
}
