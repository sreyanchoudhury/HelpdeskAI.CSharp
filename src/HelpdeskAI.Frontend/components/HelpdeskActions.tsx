"use client";

import { useFrontendTool, useCopilotReadable } from "@copilotkit/react-core";
import type { AttachedFile } from "./AttachmentBar";
import {
  DEMO_USER,
  PRIORITY_COLOR, PRIORITY_BG, CATEGORY_ICON,
  HEALTH_COLOR, HEALTH_BG, HEALTH_ICON,
  KB_CATEGORY_COLOR, KB_CAT_ICON,
} from "../lib/constants";

// ── Types ─────────────────────────────────────────────────────────────────────
export interface Ticket {
  id: string;
  title: string;
  description: string;
  priority: "low" | "medium" | "high" | "critical";
  category: string;
  status: "open" | "in_progress" | "resolved";
  createdAt: Date;
}

interface Props {
  tickets: Ticket[];
  onTicketCreated: (ticket: Ticket) => void;
  attachedFiles: AttachedFile[];
}

// ── TicketCard ────────────────────────────────────────────────────────────────
function TicketCard({ ticket, status }: { ticket: Partial<Ticket>; status: string }) {
  const priority = (ticket.priority ?? "medium") as Ticket["priority"];
  const category = (ticket.category ?? "other").toLowerCase();
  const icon = CATEGORY_ICON[category] ?? "🎫";
  const isStreaming = status === "inProgress";

  return (
    <div style={{
      border: `1px solid ${PRIORITY_COLOR[priority]}44`,
      borderLeft: `3px solid ${PRIORITY_COLOR[priority]}`,
      borderRadius: 10, padding: "14px 16px",
      background: "#0f1117", maxWidth: 420, marginTop: 4,
      opacity: isStreaming ? 0.7 : 1, transition: "opacity 0.3s",
    }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 10 }}>
        <span style={{ fontSize: 18 }}>{icon}</span>
        <span style={{
          fontSize: 11, fontWeight: 700, letterSpacing: "0.06em",
          textTransform: "uppercase", color: PRIORITY_COLOR[priority],
          background: PRIORITY_BG[priority], padding: "2px 8px", borderRadius: 4,
        }}>{priority}</span>
        <span style={{ marginLeft: "auto", fontSize: 11, color: "#5a6280", fontFamily: "monospace" }}>
          {ticket.id ?? "…"}
        </span>
      </div>
      <div style={{ fontSize: 14, fontWeight: 600, color: "#e8eaf0", marginBottom: 4 }}>
        {ticket.title ?? "Creating ticket…"}
      </div>
      {ticket.description && (
        <div style={{ fontSize: 12, color: "#9098b0", lineHeight: 1.5 }}>{ticket.description}</div>
      )}
      <div style={{
        marginTop: 12, paddingTop: 10, borderTop: "1px solid #ffffff0f",
        display: "flex", alignItems: "center", gap: 6, fontSize: 11, color: "#5a6280",
      }}>
        <span style={{ width: 6, height: 6, borderRadius: "50%", background: "#3d5afe", display: "inline-block" }} />
        {isStreaming ? "Opening ticket…" : "Ticket opened · IT Support team notified"}
      </div>
    </div>
  );
}

// ── IncidentAlertCard ─────────────────────────────────────────────────────────
function IncidentAlertCard({ incidents, status }: {
  incidents: Array<{ service: string; severity: string; message: string; incidentId?: string; workaround?: string; eta?: string }>;
  status: string;
}) {
  const isStreaming = status === "inProgress";
  if (!incidents?.length) return null;

  return (
    <div style={{
      border: "1px solid #ef444433",
      borderLeft: "3px solid #ef4444",
      borderRadius: 10, padding: "14px 16px",
      background: "#0f1117", maxWidth: 460, marginTop: 4,
      opacity: isStreaming ? 0.7 : 1, transition: "opacity 0.3s",
    }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 12 }}>
        <span style={{ fontSize: 16 }}>🚨</span>
        <span style={{
          fontSize: 11, fontWeight: 700, letterSpacing: "0.06em",
          textTransform: "uppercase", color: "#ef4444",
          background: "#ef444418", padding: "2px 8px", borderRadius: 4,
        }}>
          Active Incident{incidents.length > 1 ? "s" : ""}
        </span>
        <span style={{ marginLeft: "auto", fontSize: 11, color: "#5a6280" }}>
          {incidents.length} affected
        </span>
      </div>

      <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
        {incidents.map((inc, i) => {
          const sev = (inc.severity ?? "outage").toLowerCase();
          const color = HEALTH_COLOR[sev] ?? "#ef4444";
          const bg    = HEALTH_BG[sev]    ?? "#ef444418";
          const icon  = HEALTH_ICON[sev]  ?? "🔴";
          return (
            <div key={i} style={{
              padding: "10px 12px", borderRadius: 8,
              background: "#ffffff06", border: "1px solid #ffffff0a",
            }}>
              <div style={{ display: "flex", alignItems: "center", gap: 6, marginBottom: 4 }}>
                <span style={{ fontSize: 13 }}>{icon}</span>
                <span style={{ fontSize: 13, fontWeight: 600, color: "#e8eaf0" }}>{inc.service}</span>
                <span style={{
                  marginLeft: "auto", fontSize: 10, fontWeight: 700,
                  textTransform: "uppercase", color, background: bg,
                  padding: "1px 6px", borderRadius: 4,
                }}>{inc.severity}</span>
              </div>
              {inc.message && (
                <div style={{ fontSize: 12, color: "#9098b0", marginBottom: inc.workaround ? 6 : 0 }}>
                  {inc.message}
                </div>
              )}
              {inc.workaround && (
                <div style={{
                  fontSize: 11, color: "#22c55e",
                  background: "#22c55e0d", borderRadius: 6,
                  padding: "5px 8px", marginTop: 4,
                }}>
                  💡 Workaround: {inc.workaround}
                </div>
              )}
              {inc.incidentId && (
                <div style={{ fontSize: 10, color: "#3a4060", marginTop: 4, fontFamily: "monospace" }}>
                  {inc.incidentId}{inc.eta ? ` · ETA ${inc.eta}` : ""}
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}

// ── TicketListCard ────────────────────────────────────────────────────────────
function TicketListCard({ tickets: ticketList, status }: {
  tickets: Array<{ id: string; title: string; priority: string; status: string; category?: string }>;
  status: string;
}) {
  const isStreaming = status === "inProgress";
  if (!ticketList?.length) return (
    <div style={{
      border: "1px solid #ffffff12", borderRadius: 10, padding: "14px 16px",
      background: "#0f1117", maxWidth: 420, marginTop: 4,
      fontSize: 12, color: "#5a6280",
    }}>
      No tickets found.
    </div>
  );

  return (
    <div style={{
      border: "1px solid #ffffff12", borderLeft: "3px solid #3d5afe",
      borderRadius: 10, padding: "14px 16px",
      background: "#0f1117", maxWidth: 460, marginTop: 4,
      opacity: isStreaming ? 0.7 : 1, transition: "opacity 0.3s",
    }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 12 }}>
        <span style={{ fontSize: 14 }}>📋</span>
        <span style={{
          fontSize: 11, fontWeight: 700, letterSpacing: "0.06em",
          textTransform: "uppercase", color: "#3d5afe",
          background: "#3d5afe18", padding: "2px 8px", borderRadius: 4,
        }}>Your Tickets</span>
        <span style={{ marginLeft: "auto", fontSize: 11, color: "#5a6280" }}>
          {ticketList.length} found
        </span>
      </div>

      <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
        {ticketList.map((t, i) => {
          const pri = (t.priority ?? "medium").toLowerCase() as Ticket["priority"];
          const priColor = PRIORITY_COLOR[pri] ?? "#f59e0b";
          const cat = (t.category ?? "other").toLowerCase();
          const catIcon = CATEGORY_ICON[cat] ?? "🎫";
          const statusColor = t.status?.toLowerCase().includes("resolved") ? "#22c55e"
            : t.status?.toLowerCase().includes("progress") ? "#3d5afe" : "#9098b0";
          return (
            <div key={i} style={{
              display: "flex", alignItems: "center", gap: 10,
              padding: "8px 10px", borderRadius: 8,
              background: "#ffffff04", border: "1px solid #ffffff08",
            }}>
              <span style={{ fontSize: 14 }}>{catIcon}</span>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{
                  fontSize: 12, fontWeight: 500, color: "#e8eaf0",
                  overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap",
                }}>{t.title}</div>
                <div style={{ fontSize: 10, color: statusColor, marginTop: 1 }}>
                  {t.status}
                </div>
              </div>
              <div style={{ textAlign: "right", flexShrink: 0 }}>
                <div style={{
                  fontSize: 10, fontWeight: 700, textTransform: "uppercase",
                  color: priColor, background: `${priColor}18`,
                  padding: "1px 6px", borderRadius: 4,
                }}>{pri}</div>
                <div style={{ fontSize: 10, color: "#3a4060", fontFamily: "monospace", marginTop: 2 }}>
                  {t.id}
                </div>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

// ── AttachmentDocCard ────────────────────────────────────────────────────────
function AttachmentDocCard({ fileName, summary, blobUrl, status }: {
  fileName: string; summary: string; blobUrl?: string; status: string;
}) {
  const isStreaming = status === "inProgress";
  return (
    <div style={{
      border: "1px solid #22c55e33", borderLeft: "3px solid #22c55e",
      borderRadius: 10, padding: "14px 16px",
      background: "#0f1117", maxWidth: 420, marginTop: 4,
      opacity: isStreaming ? 0.7 : 1, transition: "opacity 0.3s",
    }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 8 }}>
        <span style={{ fontSize: 18 }}>📄</span>
        <span style={{
          fontSize: 11, fontWeight: 700, letterSpacing: "0.06em",
          textTransform: "uppercase", color: "#22c55e",
          background: "#22c55e18", padding: "2px 8px", borderRadius: 4,
        }}>Attachment Read</span>
      </div>
      <div style={{ fontSize: 13, fontWeight: 600, color: "#e8eaf0", marginBottom: 4 }}>
        {fileName}
      </div>
      {summary && (
        <div style={{ fontSize: 12, color: "#9098b0", lineHeight: 1.5 }}>{summary}</div>
      )}
      {blobUrl && (
        <a href={blobUrl} target="_blank" rel="noopener noreferrer" style={{
          display: "inline-block", marginTop: 8,
          fontSize: 11, color: "#3d5afe", textDecoration: "underline",
        }}>View original ↗</a>
      )}
    </div>
  );
}

// ── TicketDetailCard ──────────────────────────────────────────────────────────
function TicketDetailCard({ id, title, description, priority, category, ticketStatus, assignedTo, createdAt, status }: {
  id: string; title: string; description?: string; priority: string; category?: string;
  ticketStatus: string; assignedTo?: string; createdAt?: string; status: string;
}) {
  const isStreaming = status === "inProgress";
  const pri = (priority ?? "medium").toLowerCase() as Ticket["priority"];
  const priColor = PRIORITY_COLOR[pri] ?? "#f59e0b";
  const cat = (category ?? "other").toLowerCase();
  const catIcon = CATEGORY_ICON[cat] ?? "🎫";
  const statusColor = (ticketStatus ?? "").toLowerCase().includes("resolved") ? "#22c55e"
    : (ticketStatus ?? "").toLowerCase().includes("progress") ? "#3d5afe" : "#9098b0";
  return (
    <div style={{
      border: "1px solid #3d5afe33", borderLeft: "3px solid #3d5afe",
      borderRadius: 10, padding: "14px 16px",
      background: "#0f1117", maxWidth: 460, marginTop: 4,
      opacity: isStreaming ? 0.7 : 1, transition: "opacity 0.3s",
    }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 12 }}>
        <span style={{ fontSize: 16 }}>{catIcon}</span>
        <span style={{
          fontSize: 11, fontWeight: 700, letterSpacing: "0.06em",
          textTransform: "uppercase", color: "#3d5afe",
          background: "#3d5afe18", padding: "2px 8px", borderRadius: 4,
        }}>Ticket Details</span>
        <span style={{ marginLeft: "auto", fontFamily: "monospace", fontSize: 10, color: "#3a4060" }}>{id}</span>
      </div>

      <div style={{ fontSize: 14, fontWeight: 600, color: "#e8eaf0", marginBottom: 6 }}>{title}</div>

      {description && (
        <div style={{
          fontSize: 12, color: "#9098b0", lineHeight: 1.6, marginBottom: 10,
          padding: "8px 10px", background: "#ffffff04",
          borderRadius: 6, border: "1px solid #ffffff08",
        }}>{description}</div>
      )}

      <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
        <span style={{
          fontSize: 10, fontWeight: 700, textTransform: "uppercase",
          color: priColor, background: `${priColor}18`,
          padding: "2px 8px", borderRadius: 4,
        }}>{priority}</span>
        <span style={{
          fontSize: 10, fontWeight: 600, textTransform: "capitalize",
          color: statusColor, background: `${statusColor}18`,
          padding: "2px 8px", borderRadius: 4,
        }}>{ticketStatus}</span>
        {assignedTo && (
          <span style={{
            fontSize: 10, color: "#9098b0",
            background: "#ffffff08", padding: "2px 8px", borderRadius: 4,
          }}>👤 {assignedTo}</span>
        )}
      </div>

      {createdAt && (
        <div style={{ fontSize: 10, color: "#3a4060", marginTop: 8, fontFamily: "monospace" }}>
          Created: {createdAt}
        </div>
      )}
    </div>
  );
}

// ── KbArticleCard ─────────────────────────────────────────────────────────────
function KbArticleCard({ id, title, content, category, status }: {
  id: string; title: string; content: string; category?: string; status: string;
}) {
  const isStreaming = status === "inProgress";
  const catColor = KB_CATEGORY_COLOR[(category ?? "other").toLowerCase()] ?? "#9098b0";
  return (
    <div style={{
      border: "1px solid #6366f133", borderLeft: "3px solid #6366f1",
      borderRadius: 10, padding: "14px 16px",
      background: "#0f1117", maxWidth: 460, marginTop: 4,
      opacity: isStreaming ? 0.7 : 1, transition: "opacity 0.3s",
    }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 10 }}>
        <span style={{ fontSize: 14 }}>📖</span>
        <span style={{
          fontSize: 11, fontWeight: 700, letterSpacing: "0.06em",
          textTransform: "uppercase", color: "#6366f1",
          background: "#6366f118", padding: "2px 8px", borderRadius: 4,
        }}>KB Article</span>
        {category && (
          <span style={{
            fontSize: 10, color: catColor, background: `${catColor}18`,
            padding: "1px 6px", borderRadius: 4, textTransform: "capitalize",
          }}>{category}</span>
        )}
      </div>

      <div style={{ fontSize: 13, fontWeight: 600, color: "#e8eaf0", marginBottom: 8 }}>{title}</div>

      <div style={{
        fontSize: 12, color: "#9098b0", lineHeight: 1.6,
        maxHeight: 200, overflowY: "auto",
        padding: "8px 10px", background: "#ffffff04",
        borderRadius: 6, border: "1px solid #ffffff08",
      }}>{content}</div>

      <div style={{ fontSize: 10, color: "#3a4060", marginTop: 8, fontFamily: "monospace" }}>{id}</div>
    </div>
  );
}

// ── RelatedArticlesCard ───────────────────────────────────────────────────────
function RelatedArticlesCard({ articles, status }: {
  articles: Array<{ id: string; title: string; category?: string; summary?: string }>;
  status: string;
}) {
  const isStreaming = status === "inProgress";
  if (!articles?.length) return null;
  return (
    <div style={{
      border: "1px solid #6366f133", borderLeft: "3px solid #6366f1",
      borderRadius: 10, padding: "14px 16px",
      background: "#0f1117", maxWidth: 460, marginTop: 4,
      opacity: isStreaming ? 0.7 : 1, transition: "opacity 0.3s",
    }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 12 }}>
        <span style={{ fontSize: 14 }}>📚</span>
        <span style={{
          fontSize: 11, fontWeight: 700, letterSpacing: "0.06em",
          textTransform: "uppercase", color: "#6366f1",
          background: "#6366f118", padding: "2px 8px", borderRadius: 4,
        }}>Related Articles</span>
        <span style={{ marginLeft: "auto", fontSize: 11, color: "#5a6280" }}>
          {articles.length} found
        </span>
      </div>

      <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
        {articles.map((a, i) => {
          const icon = KB_CAT_ICON[(a.category ?? "other").toLowerCase()] ?? "📚";
          return (
            <div key={i} style={{
              padding: "10px 12px", borderRadius: 8,
              background: "#ffffff04", border: "1px solid #ffffff08",
            }}>
              <div style={{ display: "flex", alignItems: "flex-start", gap: 8 }}>
                <span style={{ fontSize: 14, flexShrink: 0, marginTop: 1 }}>{icon}</span>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{
                    fontSize: 12, fontWeight: 600, color: "#e8eaf0",
                    overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap",
                  }}>{a.title}</div>
                  {a.summary && (
                    <div style={{ fontSize: 11, color: "#9098b0", marginTop: 2, lineHeight: 1.4 }}>
                      {a.summary}
                    </div>
                  )}
                </div>
                <span style={{
                  fontSize: 9, color: "#3a4060", fontFamily: "monospace",
                  background: "#ffffff08", padding: "2px 6px", borderRadius: 4,
                  flexShrink: 0, marginTop: 1,
                }}>{a.id}</span>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

// ── HelpdeskActions ───────────────────────────────────────────────────────────
export function HelpdeskActions({ tickets, onTicketCreated, attachedFiles }: Props) {

  // ── Readable context ──────────────────────────────────────────────────────
  useCopilotReadable({
    description: "Current logged-in user information",
    value: {
      name: "Alex Johnson",
      department: "Engineering",
      role: "Senior Developer",
      device: "MacBook Pro 16\" (M3)",
      os: "macOS Sonoma 14.3",
      location: "Kolkata Office",
      email: DEMO_USER,
    },
  });

  useCopilotReadable({
    description: "Files attached by the user in the current message (staged, not yet consumed by the agent)",
    value: attachedFiles
      .filter(f => !f.uploading && !f.error)
      .map(f => ({ name: f.name, contentType: f.contentType, blobUrl: f.blobUrl })),
  });

  // ── Action 1: show_ticket_created ─────────────────────────────────────────
  // Renders a visual confirmation card after ticket creation.
  // Named separately from the MCP tool to prevent duplicate filtering.
  useFrontendTool({
    name: "show_ticket_created",
    description: "Renders a visual ticket card in the chat after a ticket has been created. Call this immediately after the create_ticket tool returns a ticket ID, passing the ticket details so the user can see a confirmation card.",
    parameters: [
      { name: "id",          type: "string", description: "Ticket ID returned by create_ticket", required: true },
      { name: "title",       type: "string", description: "Short title of the issue", required: true },
      { name: "description", type: "string", description: "Full description of the issue", required: true },
      { name: "priority",    type: "string", description: "low | medium | high | critical", required: true },
      { name: "category",    type: "string", description: "hardware | software | network | access | email | vpn | other", required: true },
    ],
    render: ({ status, args }) => (
      <TicketCard ticket={args as Partial<Ticket>} status={status} />
    ),
    handler: ({ id, title, description, priority, category }) => {
      const ticket: Ticket = {
        id, title, description,
        priority: priority as Ticket["priority"],
        category, status: "open", createdAt: new Date(),
      };
      onTicketCreated(ticket);
      return `Ticket card displayed for ${ticket.id}.`;
    },
  });

  // ── Action 2: show_incident_alert ─────────────────────────────────────────
  useFrontendTool({
    name: "show_incident_alert",
    description: "Renders a visual incident alert card in the chat when active IT incidents are found. Call this immediately after get_system_status, get_active_incidents, or check_impact_for_team returns incidents, instead of only describing them in plain text.",
    parameters: [
      {
        name: "incidents",
        type: "string",
        description: 'JSON array of incidents. Each item: { "service": string, "severity": "outage|degraded|maintenance", "message": string, "incidentId"?: string, "workaround"?: string, "eta"?: string }',
        required: true,
      },
    ],
    render: ({ status, args }) => {
      let parsed: Array<{ service: string; severity: string; message: string; incidentId?: string; workaround?: string; eta?: string }> = [];
      try {
        const raw = args.incidents as string;
        parsed = Array.isArray(raw) ? raw : JSON.parse(raw);
      } catch {
        parsed = [];
      }
      return <IncidentAlertCard incidents={parsed} status={status} />;
    },
    handler: () => "Incident alert displayed to user.",
  });

  // ── Action 3: show_my_tickets ─────────────────────────────────────────────
  useFrontendTool({
    name: "show_my_tickets",
    description: "Renders a visual ticket list card in the chat. Call this after search_tickets or get_ticket returns results, to show the user their tickets in a readable card instead of plain text.",
    parameters: [
      {
        name: "tickets",
        type: "string",
        description: 'JSON array of tickets. Each item: { "id": string, "title": string, "priority": string, "status": string, "category"?: string }',
        required: true,
      },
    ],
    render: ({ status, args }) => {
      let parsed: Array<{ id: string; title: string; priority: string; status: string; category?: string }> = [];
      try {
        const raw = args.tickets as unknown;
        const intermediate = Array.isArray(raw) ? raw
          : typeof raw === "string" ? JSON.parse(raw)
          : raw;
        // Agent may pass the full search_tickets response {count, tickets:[...]} instead of just the array
        parsed = Array.isArray(intermediate) ? intermediate : ((intermediate as { tickets?: unknown[] })?.tickets ?? []) as typeof parsed;
      } catch {
        parsed = [];
      }
      return <TicketListCard tickets={parsed} status={status} />;
    },
    handler: () => "Ticket list displayed to user.",
  });

  // ── Action 4: show_attachment_preview ─────────────────────────────────────
  useFrontendTool({
    name: "show_attachment_preview",
    description: "Renders a visual confirmation card after reading an attached document. Call this after you have processed an '## Attached Document' section in your context, passing a brief one-sentence summary of what the document contains.",
    parameters: [
      { name: "fileName", type: "string", description: "Name of the attached file", required: true },
      { name: "summary",  type: "string", description: "One-sentence summary of the document content", required: true },
      { name: "blobUrl",  type: "string", description: "Azure Blob Storage URL for the file", required: false },
    ],
    render: ({ status, args }) => (
      <AttachmentDocCard
        fileName={(args.fileName as string) ?? ""}
        summary={(args.summary as string) ?? ""}
        blobUrl={args.blobUrl as string | undefined}
        status={status}
      />
    ),
    handler: ({ fileName }) => `Attachment preview rendered for ${fileName}.`,
  });

  // ── Action 5: show_ticket_details ─────────────────────────────────────────
  useFrontendTool({
    name: "show_ticket_details",
    description: "Renders a detailed ticket card inline in the chat for a specific ticket. ALWAYS call this after get_ticket returns results to display the full ticket information as a card instead of plain text.",
    parameters: [
      { name: "id",          type: "string", description: "Ticket ID", required: true },
      { name: "title",       type: "string", description: "Ticket title", required: true },
      { name: "description", type: "string", description: "Full description of the issue", required: true },
      { name: "priority",    type: "string", description: "low | medium | high | critical", required: true },
      { name: "category",    type: "string", description: "hardware | software | network | access | email | vpn | other", required: true },
      { name: "status",      type: "string", description: "open | in_progress | resolved | closed", required: true },
      { name: "assignedTo",  type: "string", description: "Name or email of the assigned engineer", required: false },
      { name: "createdAt",   type: "string", description: "ISO date string when the ticket was created", required: false },
    ],
    render: ({ status, args }) => (
      <TicketDetailCard
        id={(args.id as string) ?? ""}
        title={(args.title as string) ?? ""}
        description={args.description as string | undefined}
        priority={(args.priority as string) ?? "medium"}
        category={args.category as string | undefined}
        ticketStatus={(args.status as string) ?? ""}
        assignedTo={args.assignedTo as string | undefined}
        createdAt={args.createdAt as string | undefined}
        status={status}
      />
    ),
    handler: ({ id }) => `Ticket detail card displayed for ${id}.`,
  });

  // ── Action 6: show_kb_article ─────────────────────────────────────────────
  useFrontendTool({
    name: "show_kb_article",
    description: "Renders a KB article card inline in the chat. Call this when presenting a knowledge base article to the user so they can read it directly in the conversation instead of receiving plain text.",
    parameters: [
      { name: "id",       type: "string", description: "KB article ID (e.g. KB-up-...)", required: true },
      { name: "title",    type: "string", description: "Title of the KB article", required: true },
      { name: "content",  type: "string", description: "Full article content", required: true },
      { name: "category", type: "string", description: "VPN | Email | Hardware | Network | Access | Printing | Software | Other", required: false },
    ],
    render: ({ status, args }) => (
      <KbArticleCard
        id={(args.id as string) ?? ""}
        title={(args.title as string) ?? ""}
        content={(args.content as string) ?? ""}
        category={args.category as string | undefined}
        status={status}
      />
    ),
    handler: ({ id }) => `KB article card displayed for ${id}.`,
  });

  // ── Action 7: suggest_related_articles ────────────────────────────────────
  useFrontendTool({
    name: "suggest_related_articles",
    description: "Renders a list of related KB article suggestions inline in the chat. Call this when you have 2–3 relevant KB articles to recommend to the user based on their issue.",
    parameters: [
      {
        name: "articles",
        type: "string",
        description: 'JSON array of articles. Each item: { "id": string, "title": string, "category"?: string, "summary"?: string }',
        required: true,
      },
    ],
    render: ({ status, args }) => {
      let parsed: Array<{ id: string; title: string; category?: string; summary?: string }> = [];
      try {
        const raw = args.articles as string;
        parsed = Array.isArray(raw) ? raw : JSON.parse(raw);
      } catch {
        parsed = [];
      }
      return <RelatedArticlesCard articles={parsed} status={status} />;
    },
    handler: () => "Related articles card displayed.",
  });

  return null;
}
