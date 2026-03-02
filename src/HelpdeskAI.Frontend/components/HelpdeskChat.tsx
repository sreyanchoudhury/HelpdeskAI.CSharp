"use client";

import { useState } from "react";
import { CopilotChat, CopilotKitCSSProperties } from "@copilotkit/react-ui";
import "@copilotkit/react-ui/styles.css";
import { HelpdeskActions, Ticket } from "./HelpdeskActions";

type Page = "chat" | "tickets" | "kb" | "settings";

const ckTheme: CopilotKitCSSProperties = {
  "--copilot-kit-primary-color":            "#3d5afe",
  "--copilot-kit-contrast-color":           "#ffffff",
  "--copilot-kit-background-color":         "#0a0b0f",
  "--copilot-kit-input-background-color":   "#1c2030",
  "--copilot-kit-secondary-color":          "#151820",
  "--copilot-kit-secondary-contrast-color": "#e8eaf0",
  "--copilot-kit-separator-color":          "#ffffff18",
  "--copilot-kit-muted-color":              "#5a6280",
  "--copilot-kit-shadow-sm":                "none",
  "--copilot-kit-shadow-md":                "none",
  "--copilot-kit-shadow-lg":                "none",
};

const NAV_ITEMS: { id: Page; icon: string; label: string; subtitle: string }[] = [
  { id: "chat",     icon: "💬", label: "IT Support",     subtitle: "Ask me anything about your IT issues" },
  { id: "tickets",  icon: "📋", label: "My Tickets",     subtitle: "View and track your support tickets" },
  { id: "kb",       icon: "📚", label: "Knowledge Base", subtitle: "Browse IT articles and guides" },
  { id: "settings", icon: "⚙️", label: "Settings",       subtitle: "Manage your preferences" },
];

const PRIORITY_COLOR: Record<Ticket["priority"], string> = {
  low: "#22c55e", medium: "#f59e0b", high: "#ef4444", critical: "#9333ea",
};

function TicketsPage({ tickets }: { tickets: Ticket[] }) {
  if (tickets.length === 0) {
    return (
      <div style={{
        flex: 1, display: "flex", flexDirection: "column",
        alignItems: "center", justifyContent: "center",
        gap: 12, color: "#5a6280", fontSize: 13,
      }}>
        <span style={{ fontSize: 40, opacity: 0.3 }}>📋</span>
        No tickets yet — ask the agent to create one for you.
      </div>
    );
  }
  return (
    <div style={{ flex: 1, overflowY: "auto", padding: "24px 32px" }}>
      <div style={{ display: "flex", flexDirection: "column", gap: 12, maxWidth: 640 }}>
        {tickets.map(t => (
          <div key={t.id} style={{
            background: "#0f1117",
            border: `1px solid ${PRIORITY_COLOR[t.priority]}33`,
            borderLeft: `3px solid ${PRIORITY_COLOR[t.priority]}`,
            borderRadius: 10, padding: "14px 18px",
            display: "flex", alignItems: "center", gap: 16,
          }}>
            <div style={{ flex: 1, minWidth: 0 }}>
              <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 4 }}>
                <span style={{ fontSize: 13, fontWeight: 600, color: "#e8eaf0" }}>{t.title}</span>
                <span style={{
                  fontSize: 10, fontWeight: 700, letterSpacing: "0.06em",
                  textTransform: "uppercase", color: PRIORITY_COLOR[t.priority],
                  background: `${PRIORITY_COLOR[t.priority]}18`,
                  padding: "1px 6px", borderRadius: 4,
                }}>{t.priority}</span>
              </div>
              <div style={{ fontSize: 12, color: "#5a6280" }}>{t.description.slice(0, 80)}…</div>
            </div>
            <div style={{ textAlign: "right", flexShrink: 0 }}>
              <div style={{ fontSize: 11, fontFamily: "monospace", color: "#3d5afe" }}>{t.id}</div>
              <div style={{ fontSize: 11, color: "#5a6280", marginTop: 2 }}>
                {t.createdAt.toLocaleDateString()}
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function Placeholder({ icon, title }: { icon: string; title: string }) {
  return (
    <div style={{
      flex: 1, display: "flex", flexDirection: "column",
      alignItems: "center", justifyContent: "center",
      gap: 12, textAlign: "center", padding: "60px 20px",
    }}>
      <span style={{ fontSize: 48, opacity: 0.5 }}>{icon}</span>
      <h2 style={{ fontSize: 18, fontWeight: 600, color: "#9098b0" }}>{title}</h2>
      <p style={{ fontSize: 13, color: "#5a6280" }}>This section is coming soon.</p>
    </div>
  );
}

export function HelpdeskChat() {
  const [page, setPage]     = useState<Page>("chat");
  const [tickets, setTickets] = useState<Ticket[]>([]);
  const current = NAV_ITEMS.find(n => n.id === page)!;

  const handleTicketCreated = (ticket: Ticket) => {
    setTickets(prev => [ticket, ...prev]);
  };

  return (
    <div className="hd-shell">
      <aside className="hd-sidebar">
        <div className="hd-logo">
          <svg width="28" height="28" viewBox="0 0 28 28" fill="none">
            <rect width="28" height="28" rx="8" fill="#3d5afe" />
            <path d="M8 14a6 6 0 0112 0" stroke="white" strokeWidth="2" strokeLinecap="round"/>
            <circle cx="8" cy="15" r="2.5" fill="white"/>
            <circle cx="20" cy="15" r="2.5" fill="white"/>
          </svg>
          <span className="hd-logo-text">HelpdeskAI</span>
        </div>
        <nav className="hd-nav">
          {NAV_ITEMS.map(item => (
            <button
              key={item.id}
              className={`hd-nav-item ${page === item.id ? "hd-nav-item--active" : ""}`}
              onClick={() => setPage(item.id)}
            >
              <span>{item.icon}</span>
              {item.label}
              {item.id === "tickets" && tickets.length > 0 && (
                <span style={{
                  marginLeft: "auto",
                  background: "#3d5afe",
                  color: "#fff",
                  fontSize: 10,
                  fontWeight: 700,
                  borderRadius: 10,
                  padding: "1px 6px",
                  minWidth: 18,
                  textAlign: "center",
                }}>
                  {tickets.length}
                </span>
              )}
            </button>
          ))}
        </nav>
        <div className="hd-sidebar-footer">
          <div className="hd-powered">Powered by <strong>Microsoft Agents</strong></div>
        </div>
      </aside>

      <main className="hd-main">
        <header className="hd-header">
          <div>
            <h1 className="hd-title">{current.label}</h1>
            <p className="hd-subtitle">{current.subtitle}</p>
          </div>
        </header>

        {page === "chat" && (
          <>
            <HelpdeskActions tickets={tickets} onTicketCreated={handleTicketCreated} />

            <div className="hd-chat-wrapper" style={ckTheme}>
              <CopilotChat
                instructions={`You are an IT helpdesk assistant for Alex Johnson (Senior Developer, Engineering, Kolkata Office).
Email: alex.johnson@contoso.com

## MCP tools (fetch data from backend)
- get_system_status / get_active_incidents / check_impact_for_team: check IT service health
- create_ticket / get_ticket / search_tickets / update_ticket_status / add_ticket_comment: manage support tickets

## Frontend render actions — MUST use these to display results visually
- show_incident_alert: ALWAYS call this after get_active_incidents or get_system_status returns any incidents. Pass incidents as a JSON array. Never reply with plain text incident data.
- show_my_tickets: ALWAYS call this after search_tickets or get_ticket returns results. Pass tickets as a JSON array. Never reply with plain text ticket lists.
- create_ticket: Call when the user wants to log or track an issue.

## Rules
1. When asked about incidents, outages, or system status — call get_active_incidents THEN call show_incident_alert with the results.
2. When asked to see tickets — call search_tickets THEN call show_my_tickets with the results.
3. Always check get_system_status before troubleshooting — there may be an active incident causing the issue.
4. Even if you provide a text explanation, still call the render action so results appear as a card.`}
                labels={{
                  title: "IT Support",
                  initial: "👋 Hi Alex! What IT issue can I help you with today?",
                  placeholder: "Describe your IT issue…",
                }}
              />
            </div>
          </>
        )}

        {page === "tickets"  && <TicketsPage tickets={tickets} />}
        {page === "kb"       && <Placeholder icon="📚" title="Knowledge Base" />}
        {page === "settings" && <Placeholder icon="⚙️" title="Settings" />}
      </main>
    </div>
  );
}