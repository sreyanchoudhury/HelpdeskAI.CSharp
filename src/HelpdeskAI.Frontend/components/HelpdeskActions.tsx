"use client";

import { useCopilotAction, useCopilotReadable, useCopilotChatSuggestions } from "@copilotkit/react-core";

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
}

// ── Shared styles ─────────────────────────────────────────────────────────────
const PRIORITY_COLOR: Record<Ticket["priority"], string> = {
  low: "#22c55e", medium: "#f59e0b", high: "#ef4444", critical: "#9333ea",
};
const PRIORITY_BG: Record<Ticket["priority"], string> = {
  low: "#22c55e18", medium: "#f59e0b18", high: "#ef444418", critical: "#9333ea18",
};
const CATEGORY_ICON: Record<string, string> = {
  hardware: "🖥️", software: "💻", network: "🌐",
  access: "🔑", email: "📧", vpn: "🔒", other: "🎫",
};
const HEALTH_COLOR: Record<string, string> = {
  outage: "#ef4444", degraded: "#f59e0b", maintenance: "#3d5afe", operational: "#22c55e",
};
const HEALTH_BG: Record<string, string> = {
  outage: "#ef444418", degraded: "#f59e0b18", maintenance: "#3d5afe18", operational: "#22c55e18",
};
const HEALTH_ICON: Record<string, string> = {
  outage: "🔴", degraded: "⚠️", maintenance: "🔧", operational: "✅",
};

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

// ── HelpdeskActions ───────────────────────────────────────────────────────────
export function HelpdeskActions({ tickets, onTicketCreated }: Props) {

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
      email: "alex.johnson@contoso.com",
    },
  });

  useCopilotReadable({
    description: "Current open support tickets for this user",
    value: tickets.map(t => ({
      id: t.id, title: t.title, priority: t.priority,
      status: t.status, createdAt: t.createdAt.toISOString(),
    })),
  });

  // ── Action 1: show_ticket_created ─────────────────────────────────────────
  // Renders a visual confirmation card after ticket creation.
  // Named separately from the MCP tool to prevent duplicate filtering.
  useCopilotAction({
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
  useCopilotAction({
    name: "show_incident_alert",
    description: "Renders a visual incident alert card in the chat when active IT incidents are found. Call this after get_system_status or get_active_incidents returns degraded or outage services, to present the information clearly to the user instead of plain text.",
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
  useCopilotAction({
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
        const raw = args.tickets as string;
        parsed = Array.isArray(raw) ? raw : JSON.parse(raw);
      } catch {
        parsed = [];
      }
      return <TicketListCard tickets={parsed} status={status} />;
    },
    handler: () => "Ticket list displayed to user.",
  });

  // ── Suggestions ───────────────────────────────────────────────────────────
  useCopilotChatSuggestions({
    instructions: "Suggest 3 short follow-up questions for an IT helpdesk user. Under 8 words each. Focus on: VPN issues, ticket status, system outages, software installs, access requests.",
    maxSuggestions: 3,
  });

  return null;
}