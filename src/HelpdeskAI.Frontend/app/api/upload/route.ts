import { NextRequest, NextResponse } from "next/server";

// AGENT_URL in next.config carries the /agent suffix (for CopilotKit).
// Strip it so we can reach /api/attachments on the same host.
const AGENT_BASE = (process.env.AGENT_URL ?? "http://localhost:5200/agent")
  .replace(/\/agent\/?$/, "");

const SESSION_ID = "alex.johnson:dev-session";

export async function POST(req: NextRequest) {
  try {
    return await handleUpload(req);
  } catch (err) {
    console.error("[upload] Unhandled exception:", err);
    return NextResponse.json({ error: "Internal upload error", detail: String(err) }, { status: 500 });
  }
}

async function handleUpload(req: NextRequest) {
  const contentType = req.headers.get("content-type") ?? "";

  if (!contentType.includes("multipart/form-data")) {
    return NextResponse.json({ error: "Expected multipart/form-data" }, { status: 400 });
  }

  const formData = await req.formData();
  const file = formData.get("file");

  if (!file || !(file instanceof File)) {
    return NextResponse.json({ error: "No file provided" }, { status: 400 });
  }

  // Forward to AgentHost
  const forwardForm = new FormData();
  forwardForm.append("file", file);

  let agentResponse: Response;
  try {
    agentResponse = await fetch(`${AGENT_BASE}/api/attachments`, {
      method: "POST",
      headers: { "X-Session-Id": SESSION_ID },
      body: forwardForm,
    });
  } catch (err) {
    console.error("[upload] Failed to reach AgentHost:", err);
    return NextResponse.json({ error: "AgentHost unreachable" }, { status: 502 });
  }

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

  return NextResponse.json(body, { status: agentResponse.status });
}
