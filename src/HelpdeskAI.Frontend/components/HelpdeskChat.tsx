"use client";

import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type KeyboardEvent, type ChangeEvent } from "react";
import { CopilotChat, CopilotKitCSSProperties, type InputProps } from "@copilotkit/react-ui";
import { useCopilotContext, useCopilotChat } from "@copilotkit/react-core";
import { signOut } from "next-auth/react";
import "@copilotkit/react-ui/styles.css";
import { HelpdeskActions, type CurrentUser, Ticket } from "./HelpdeskActions";
import type { AttachedFile } from "./AttachmentBar";
import { PRIORITY_COLOR, CATEGORY_ICON, KB_CATEGORY_COLOR } from "../lib/constants";
import { CitationBadge, KB_CITATION_REGEX } from "./CitationBadge";

type Page = "chat" | "tickets" | "kb" | "settings" | "eval";

const CONTENT_SAFETY_MARKER = "blocked by Azure content safety";
const CHAT_RESET_EVENT = "helpdesk-reset-chat";
const CONTENT_SAFETY_FLASH_KEY = "helpdesk-content-safety-flash";
const CONTENT_SAFETY_MESSAGE =
  "⚠️ Your request was blocked by Azure content safety. " +
  "The conversation history for this thread has been cleared — please send a new message to continue.";

function consumePendingChatInitial(): string | null {
  if (typeof window === "undefined") return null;
  const pending = window.sessionStorage.getItem(CONTENT_SAFETY_FLASH_KEY);
  if (!pending) return null;
  window.sessionStorage.removeItem(CONTENT_SAFETY_FLASH_KEY);
  return pending;
}

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
  { id: "eval",     icon: "📊", label: "Evaluations",    subtitle: "Agent quality metrics and eval runs" },
];

interface ServerTicket {
  id: string; title: string; description: string;
  status: string; priority: string; category: string;
  requestedBy: string; assignedTo?: string;
  createdAt: string; updatedAt: string; resolution?: string;
}

interface KbArticle {
  id: string; title: string; content: string; category?: string;
}

interface ActiveIncidentSummary {
  service: string;
  severity: string;
  incidentId?: string;
  message?: string;
  workaround?: string;
  eta?: string | null;
}

interface IncidentFeedState {
  status: "loading" | "ok" | "empty" | "error";
  count: number;
  checkedAt?: string;
  error?: string;
}

const STATUS_COLOR: Record<string, string> = {
  Open: "#3d5afe", InProgress: "#f59e0b", PendingUser: "#f97316",
  Resolved: "#22c55e", Closed: "#5a6280",
};
const STATUS_LABEL: Record<string, string> = {
  Open: "Open", InProgress: "In Progress", PendingUser: "Pending",
  Resolved: "Resolved", Closed: "Closed",
};

function TicketsPage({ agentTickets, refreshKey, currentUser }: { agentTickets: Ticket[]; refreshKey: number; currentUser: CurrentUser }) {
  const [serverTickets, setServerTickets] = useState<ServerTicket[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());

  const toggleExpand = (id: string) =>
    setExpanded(prev => { const s = new Set(prev); s.has(id) ? s.delete(id) : s.add(id); return s; });

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    fetch(`/api/tickets?requestedBy=${encodeURIComponent(currentUser.email)}`)
      .then(r => (r.ok ? r.json() : Promise.reject(`HTTP ${r.status}`)))
      .then((data: ServerTicket[]) => {
        if (!cancelled) { setServerTickets(data); setLoading(false); }
      })
      .catch(e => {
        if (!cancelled) { setError(String(e)); setLoading(false); }
      });
    return () => { cancelled = true; };
  }, [currentUser.email, refreshKey]);

  const serverIds = new Set(serverTickets.map(t => t.id));
  const agentOnly: ServerTicket[] = agentTickets
    .filter(t => !serverIds.has(t.id))
    .map(t => ({
      id: t.id, title: t.title, description: t.description,
      status: t.status === "open" ? "Open" : t.status === "in_progress" ? "InProgress" : "Resolved",
      priority: t.priority.charAt(0).toUpperCase() + t.priority.slice(1),
      category: t.category,
      requestedBy: currentUser.email,
      createdAt: t.createdAt instanceof Date ? t.createdAt.toISOString() : String(t.createdAt),
      updatedAt: t.createdAt instanceof Date ? t.createdAt.toISOString() : String(t.createdAt),
    }));
  const display = [...serverTickets, ...agentOnly];

  if (loading) return (
    <div style={{ flex: 1, display: "flex", alignItems: "center", justifyContent: "center", color: "#5a6280", fontSize: 13 }}>
      Loading tickets…
    </div>
  );
  if (error) return (
    <div style={{ flex: 1, display: "flex", alignItems: "center", justifyContent: "center", color: "#ef4444", fontSize: 13 }}>
      {error}
    </div>
  );
  if (display.length === 0) return (
    <div style={{ flex: 1, display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", gap: 12, color: "#5a6280", fontSize: 13 }}>
      <span style={{ fontSize: 40, opacity: 0.3 }}>📋</span>
      No tickets yet — ask the agent to create one for you.
    </div>
  );

  return (
    <div className="hd-page-scroll">
      {/* User context hint */}
      <div style={{ fontSize: 11, color: "#5a6280", marginBottom: 12 }}>
        Showing tickets for <span style={{ color: "#3d5afe" }}>{currentUser.email}</span> — click any card to expand details
      </div>
      <div className="hd-page-stack">
        {display.map(t => {
          const priColor = PRIORITY_COLOR[t.priority.toLowerCase() as Ticket["priority"]] ?? "#5a6280";
          const stsColor = STATUS_COLOR[t.status] ?? "#5a6280";
          const catIcon  = CATEGORY_ICON[t.category.toLowerCase()] ?? "🎫";
          const isOpen   = expanded.has(t.id);
          return (
            <div
              key={t.id}
              onClick={() => toggleExpand(t.id)}
              style={{
                background: "#0f1117",
                border: `1px solid ${priColor}33`,
                borderLeft: `3px solid ${priColor}`,
                borderRadius: 10, padding: "12px 16px",
                cursor: "pointer", userSelect: "none",
                transition: "border-color 0.15s",
              }}
            >
              {/* Header row — always visible */}
              <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                <span style={{ fontSize: 12, color: "#5a6280", flexShrink: 0, transition: "transform 0.2s", display: "inline-block", transform: isOpen ? "rotate(90deg)" : "rotate(0deg)" }}>▶</span>
                <span style={{ fontSize: 13, fontWeight: 600, color: "#e8eaf0", flex: 1, minWidth: 0, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{t.title}</span>
                <span style={{ fontSize: 10, fontWeight: 700, letterSpacing: "0.05em", textTransform: "uppercase", color: priColor, background: `${priColor}18`, padding: "1px 6px", borderRadius: 4, flexShrink: 0 }}>
                  {t.priority}
                </span>
                <span style={{ fontSize: 10, fontWeight: 600, color: stsColor, background: `${stsColor}18`, padding: "1px 6px", borderRadius: 4, flexShrink: 0 }}>
                  {STATUS_LABEL[t.status] ?? t.status}
                </span>
                <span style={{ fontSize: 11, fontFamily: "monospace", color: "#3d5afe", flexShrink: 0 }}>{t.id}</span>
              </div>

              {/* Expanded detail */}
              {isOpen && (
                <div style={{ marginTop: 12, paddingTop: 12, borderTop: "1px solid #ffffff0c", display: "flex", flexDirection: "column", gap: 8 }}>
                  <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
                    <span style={{ fontSize: 11, color: "#9098b0" }}>{catIcon} {t.category}</span>
                    <span style={{ fontSize: 11, color: "#5a6280" }}>·</span>
                    <span style={{ fontSize: 11, color: "#5a6280" }}>Created {new Date(t.createdAt).toLocaleDateString()}</span>
                    {t.updatedAt !== t.createdAt && (
                      <><span style={{ fontSize: 11, color: "#5a6280" }}>·</span>
                      <span style={{ fontSize: 11, color: "#5a6280" }}>Updated {new Date(t.updatedAt).toLocaleDateString()}</span></>
                    )}
                  </div>
                  {t.description && (
                    <div style={{ fontSize: 12, color: "#9098b0", lineHeight: 1.6, background: "#ffffff04", borderRadius: 6, padding: "8px 10px", border: "1px solid #ffffff08" }}>
                      {t.description}
                    </div>
                  )}
                  {t.assignedTo && (
                    <div style={{ fontSize: 11, color: "#3d5afe" }}>👤 Assigned to: {t.assignedTo}</div>
                  )}
                  {t.resolution && (
                    <div style={{ fontSize: 12, color: "#22c55e", background: "#22c55e0c", borderRadius: 6, padding: "8px 10px", border: "1px solid #22c55e20" }}>
                      ✅ Resolution: {t.resolution}
                    </div>
                  )}
                </div>
              )}
            </div>
          );
        })}
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

function KbPage() {
  const [query, setQuery] = useState("");
  const [articles, setArticles] = useState<KbArticle[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());

  const toggleExpand = (id: string) =>
    setExpanded(prev => { const s = new Set(prev); s.has(id) ? s.delete(id) : s.add(id); return s; });

  useEffect(() => {
    let cancelled = false;
    const timer = setTimeout(() => {
      setLoading(true);
      setError(null);
      const qs = query.trim() ? `?q=${encodeURIComponent(query.trim())}` : "";
      fetch(`/api/kb${qs}`)
        .then(r => (r.ok ? r.json() : Promise.reject(`HTTP ${r.status}`)))
        .then((data: KbArticle[]) => {
          if (!cancelled) { setArticles(data); setLoading(false); }
        })
        .catch(e => {
          if (!cancelled) { setError(String(e)); setLoading(false); }
        });
    }, query.trim() ? 300 : 0);
    return () => { cancelled = true; clearTimeout(timer); };
  }, [query]);

  // Preview: first ~200 chars of content
  const preview = (text: string) => text.length > 200 ? text.slice(0, 200) + "…" : text;

  return (
    <div style={{ flex: 1, display: "flex", flexDirection: "column", overflow: "hidden" }}>
      <div style={{ padding: "16px var(--page-gutter) 8px" }}>
        <input
          type="text"
          placeholder="Search knowledge base…"
          value={query}
          onChange={e => setQuery(e.target.value)}
          style={{
            width: "100%", maxWidth: 520,
            padding: "10px 14px", background: "#1c2030",
            border: "1px solid #ffffff18", borderRadius: 8,
            color: "#e8eaf0", fontSize: 13, outline: "none",
            boxSizing: "border-box",
          }}
        />
      </div>
      <div className="hd-page-scroll" style={{ paddingTop: 8 }}>
        {loading ? (
          <div style={{ color: "#5a6280", fontSize: 13, paddingTop: 32, textAlign: "center" }}>Loading…</div>
        ) : error ? (
          <div style={{ color: "#ef4444", fontSize: 13, paddingTop: 32, textAlign: "center" }}>{error}</div>
        ) : articles.length === 0 ? (
          <div style={{ color: "#5a6280", fontSize: 13, paddingTop: 32, textAlign: "center" }}>
            {query.trim() ? "No articles found." : "No articles available."}
          </div>
        ) : (
          <div className="hd-page-stack">
            {articles.map(a => {
              const catColor = KB_CATEGORY_COLOR[(a.category ?? "other").toLowerCase()] ?? "#9098b0";
              const isOpen   = expanded.has(a.id);
              return (
                <div
                  key={a.id}
                  onClick={() => toggleExpand(a.id)}
                  style={{
                    border: "1px solid #6366f133", borderLeft: "3px solid #6366f1",
                    borderRadius: 10, padding: "12px 16px", background: "#0f1117",
                    cursor: "pointer", userSelect: "none",
                  }}
                >
                  {/* Header row */}
                  <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                    <span style={{ fontSize: 12, color: "#5a6280", flexShrink: 0, transition: "transform 0.2s", display: "inline-block", transform: isOpen ? "rotate(90deg)" : "rotate(0deg)" }}>▶</span>
                    <span style={{ fontSize: 14, marginRight: 2 }}>📖</span>
                    <span style={{ fontSize: 13, fontWeight: 600, color: "#e8eaf0", flex: 1, minWidth: 0, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{a.title}</span>
                    {a.category && (
                      <span style={{
                        fontSize: 10, color: catColor, background: `${catColor}18`,
                        padding: "1px 6px", borderRadius: 4, textTransform: "capitalize", flexShrink: 0,
                      }}>{a.category}</span>
                    )}
                  </div>

                  {/* Preview (always shown) */}
                  {!isOpen && (
                    <div style={{ fontSize: 12, color: "#5a6280", marginTop: 6, lineHeight: 1.5, paddingLeft: 20 }}>
                      {preview(a.content)}
                    </div>
                  )}

                  {/* Expanded full content */}
                  {isOpen && (
                    <div style={{ marginTop: 10, paddingTop: 10, borderTop: "1px solid #ffffff0c", paddingLeft: 20 }}>
                      <div style={{
                        fontSize: 12, color: "#9098b0", lineHeight: 1.7,
                        whiteSpace: "pre-wrap",
                        padding: "8px 10px", background: "#ffffff04",
                        borderRadius: 6, border: "1px solid #ffffff08",
                      }}>{a.content}</div>
                      <div style={{ fontSize: 10, color: "#3a4060", marginTop: 8, fontFamily: "monospace" }}>{a.id}</div>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}

function SettingsPage({ currentUser }: { currentUser: CurrentUser }) {
  const [status, setStatus] = useState<{ mcp: string; agent: string; checkedAt?: string } | null>(null);
  const [checking, setChecking] = useState(true);
  const [incidentFeed, setIncidentFeed] = useState<IncidentFeedState>({ status: "loading", count: 0 });
  const [agentMode, setAgentMode] = useState<"v1" | "v2">(() => {
    if (typeof window === "undefined") return "v1";
    return (localStorage.getItem("agent-mode") as "v1" | "v2") ?? "v1";
  });
  const [copilotControls, setCopilotControls] = useState<boolean>(() => {
    if (typeof window === "undefined") return false;
    return localStorage.getItem("copilotkit-controls") === "visible";
  });
  const [incidentBannerVisible, setIncidentBannerVisible] = useState<boolean>(() => {
    if (typeof window === "undefined") return true;
    return localStorage.getItem("incident-banner") !== "hidden";
  });
  const handleModeSwitch = (mode: "v1" | "v2") => {
    localStorage.setItem("agent-mode", mode);
    document.cookie = `agent-mode=${mode};path=/;samesite=strict;max-age=31536000`;
    setAgentMode(mode);
    window.location.reload();
  };
  const handleCopilotControlToggle = (visible: boolean) => {
    localStorage.setItem("copilotkit-controls", visible ? "visible" : "hidden");
    setCopilotControls(visible);
    window.location.reload();
  };
  const handleIncidentBannerToggle = (visible: boolean) => {
    localStorage.setItem("incident-banner", visible ? "visible" : "hidden");
    setIncidentBannerVisible(visible);
    window.location.reload();
  };
  const initials = currentUser.name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map(part => part[0]?.toUpperCase() ?? "")
    .join("") || currentUser.email.slice(0, 2).toUpperCase();
  const tenant = currentUser.email.includes("@") ? currentUser.email.split("@")[1] : "unknown";

  const check = () => {
    setChecking(true);
    fetch("/api/status", { cache: "no-store" })
      .then(r => r.json())
      .then(d => { setStatus(d); setChecking(false); })
      .catch(() => { setStatus({ mcp: "down", agent: "down" }); setChecking(false); });
  };

  useEffect(() => { check(); }, []);

  useEffect(() => {
    let cancelled = false;
    setIncidentFeed({ status: "loading", count: 0 });

    fetch("/api/incidents", { cache: "no-store" })
      .then(async r => {
        const data = await r.json().catch(() => null);
        if (!r.ok) {
          const message = typeof data?.error === "string" ? data.error : `HTTP ${r.status}`;
          throw new Error(message);
        }
        return data as { incidents?: ActiveIncidentSummary[]; checkedAt?: string };
      })
      .then(data => {
        if (cancelled) return;
        const incidents = Array.isArray(data.incidents) ? data.incidents : [];
        setIncidentFeed({
          status: incidents.length > 0 ? "ok" : "empty",
          count: incidents.length,
          checkedAt: data.checkedAt,
        });
      })
      .catch(error => {
        if (cancelled) return;
        setIncidentFeed({
          status: "error",
          count: 0,
          error: error instanceof Error ? error.message : String(error),
        });
      });

    return () => { cancelled = true; };
  }, [incidentBannerVisible]);

  const dot = (s: string) => (
    <span style={{
      display: "inline-block", width: 9, height: 9, borderRadius: "50%",
      background: s === "ok" ? "#22c55e" : checking ? "#f59e0b" : "#ef4444",
      marginRight: 8, flexShrink: 0,
    }} />
  );

  const services = [
    { label: "McpServer",  key: "mcp",   port: "5100", icon: "🔧" },
    { label: "AgentHost",  key: "agent", port: "5200", icon: "🤖" },
  ] as const;

  return (
    <div className="hd-page-scroll" style={{ paddingTop: 24 }}>
      <div className="hd-settings-stack">

        {/* User Profile */}
        <div className="hd-settings-card">
          <div style={{ fontSize: 11, fontWeight: 700, letterSpacing: "0.08em", textTransform: "uppercase", color: "#5a6280", marginBottom: 16 }}>User Profile</div>
          <div style={{ display: "flex", alignItems: "center", gap: 16 }}>
            <div style={{
              width: 48, height: 48, borderRadius: "50%",
              background: "linear-gradient(135deg, #3d5afe, #6366f1)",
              display: "flex", alignItems: "center", justifyContent: "center",
              fontSize: 18, fontWeight: 700, color: "#fff", flexShrink: 0,
            }}>{initials}</div>
            <div>
              <div style={{ fontSize: 15, fontWeight: 600, color: "#e8eaf0" }}>{currentUser.name}</div>
              <div style={{ fontSize: 12, color: "#5a6280", marginTop: 2 }}>{currentUser.email}</div>
            </div>
          </div>
          <div className="hd-user-meta-grid">
            {([
              ["Sign-In",    "Microsoft Entra ID"],
              ["Session",    "Authenticated"],
              ["Identity",   "Corporate account"],
              ["Tenant",     tenant],
            ] as [string, string][]).map(([label, value]) => (
              <div key={label}>
                <div style={{ fontSize: 10, color: "#5a6280", textTransform: "uppercase", letterSpacing: "0.07em" }}>{label}</div>
                <div style={{ fontSize: 12, color: "#9098b0", marginTop: 2 }}>{value}</div>
              </div>
            ))}
          </div>
        </div>

        {/* Agent Mode Toggle */}
        <div className="hd-settings-card">
          <div style={{ fontSize: 11, fontWeight: 700, letterSpacing: "0.08em", textTransform: "uppercase", color: "#5a6280", marginBottom: 16 }}>Agent Mode</div>
          <div className="hd-agent-mode-grid">
            {(["v1", "v2"] as const).map(mode => {
              const active = agentMode === mode;
              const label = mode === "v1" ? "Single Agent (v1)" : "Multi-Agent Workflow (v2)";
              return (
                <button key={mode} onClick={() => handleModeSwitch(mode)} style={{
                  flex: 1, padding: "12px 14px", borderRadius: 8,
                  border: `1px solid ${active ? "#3d5afe" : "#ffffff12"}`,
                  background: active ? "#3d5afe14" : "#ffffff04",
                  cursor: active ? "default" : "pointer",
                  textAlign: "left",
                }}>
                  <div style={{ fontSize: 13, fontWeight: 600, color: active ? "#3d5afe" : "#9098b0" }}>{label}</div>
                  <div style={{ fontSize: 11, color: "#5a6280", marginTop: 4, lineHeight: 1.4 }}>
                    {mode === "v1"
                      ? "All tools in one agent. Model: gpt-5.3-chat."
                      : "Orchestrator + specialist agents. Model: gpt-5.2-chat."}
                  </div>
                </button>
              );
            })}
          </div>
          <div style={{ fontSize: 10, color: "#5a6280", marginTop: 10 }}>
            Switching modes reloads the page and starts a new conversation. v2 is experimental.
          </div>
        </div>

        <div className="hd-settings-card">
          <div style={{ fontSize: 11, fontWeight: 700, letterSpacing: "0.08em", textTransform: "uppercase", color: "#5a6280", marginBottom: 16 }}>CopilotKit Controls</div>
          <div className="hd-agent-mode-grid">
            {([
              { id: false, label: "Hidden", subtitle: "Recommended for normal app usage and mobile screens." },
              { id: true, label: "Visible", subtitle: "Shows the CopilotKit developer controls and inspector." },
            ] as const).map(option => {
              const active = copilotControls === option.id;
              return (
                <button key={option.label} onClick={() => handleCopilotControlToggle(option.id)} style={{
                  flex: 1, padding: "12px 14px", borderRadius: 8,
                  border: `1px solid ${active ? "#3d5afe" : "#ffffff12"}`,
                  background: active ? "#3d5afe14" : "#ffffff04",
                  cursor: active ? "default" : "pointer",
                  textAlign: "left",
                }}>
                  <div style={{ fontSize: 13, fontWeight: 600, color: active ? "#3d5afe" : "#9098b0" }}>{option.label}</div>
                  <div style={{ fontSize: 11, color: "#5a6280", marginTop: 4, lineHeight: 1.4 }}>
                    {option.subtitle}
                  </div>
                </button>
              );
            })}
          </div>
          <div style={{ fontSize: 10, color: "#5a6280", marginTop: 10 }}>
            Changing this setting reloads the page so the CopilotKit provider can reinitialize cleanly.
          </div>
        </div>

        <div className="hd-settings-card">
          <div style={{ fontSize: 11, fontWeight: 700, letterSpacing: "0.08em", textTransform: "uppercase", color: "#5a6280", marginBottom: 16 }}>Live Incident Banner</div>
          <div className="hd-agent-mode-grid">
            {([
              { id: true, label: "Visible", subtitle: "Shows a proactive active-incident banner in the app shell." },
              { id: false, label: "Hidden", subtitle: "Keeps the interface focused on chat and manual workflows." },
            ] as const).map(option => {
              const active = incidentBannerVisible === option.id;
              return (
                <button key={option.label} onClick={() => handleIncidentBannerToggle(option.id)} style={{
                  flex: 1, padding: "12px 14px", borderRadius: 8,
                  border: `1px solid ${active ? "#3d5afe" : "#ffffff12"}`,
                  background: active ? "#3d5afe14" : "#ffffff04",
                  cursor: active ? "default" : "pointer",
                  textAlign: "left",
                }}>
                  <div style={{ fontSize: 13, fontWeight: 600, color: active ? "#3d5afe" : "#9098b0" }}>{option.label}</div>
                  <div style={{ fontSize: 11, color: "#5a6280", marginTop: 4, lineHeight: 1.4 }}>
                    {option.subtitle}
                  </div>
                </button>
              );
            })}
          </div>
          <div style={{ fontSize: 10, color: "#5a6280", marginTop: 10 }}>
            Changing this setting reloads the page so the banner state is applied consistently.
          </div>
          <div style={{
            marginTop: 12,
            fontSize: 11,
            color:
              incidentFeed.status === "ok" ? "#22c55e" :
              incidentFeed.status === "empty" ? "#9098b0" :
              incidentFeed.status === "error" ? "#ef4444" :
              "#f59e0b",
          }}>
            Live incident feed: {
              incidentFeed.status === "ok" ? `${incidentFeed.count} active incident${incidentFeed.count === 1 ? "" : "s"} available` :
              incidentFeed.status === "empty" ? "no active incidents returned" :
              incidentFeed.status === "error" ? `unavailable${incidentFeed.error ? ` (${incidentFeed.error})` : ""}` :
              "checking..."
            }
          </div>
        </div>

        {/* Backend Status */}
        <div className="hd-settings-card">
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 16 }}>
            <div style={{ fontSize: 11, fontWeight: 700, letterSpacing: "0.08em", textTransform: "uppercase", color: "#5a6280" }}>Backend Status</div>
            <button
              onClick={check}
              disabled={checking}
              style={{
                fontSize: 11, color: checking ? "#5a6280" : "#3d5afe",
                background: "none", border: "1px solid " + (checking ? "#ffffff12" : "#3d5afe44"),
                borderRadius: 6, padding: "3px 10px", cursor: checking ? "default" : "pointer",
              }}
            >
              {checking ? "Checking…" : "Refresh"}
            </button>
          </div>
          <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
            {services.map(svc => (
              <div key={svc.key} style={{
                display: "flex", alignItems: "center",
                background: "#ffffff04", borderRadius: 8,
                padding: "10px 14px", border: "1px solid #ffffff08",
              }}>
                <span style={{ fontSize: 16, marginRight: 10 }}>{svc.icon}</span>
                <div style={{ flex: 1 }}>
                  <div style={{ fontSize: 13, color: "#e8eaf0", fontWeight: 500 }}>{svc.label}</div>
                  <div style={{ fontSize: 11, color: "#5a6280" }}>:{svc.port}</div>
                </div>
                {dot(status?.[svc.key] ?? "")}
                <span style={{
                  fontSize: 11, fontWeight: 600,
                  color: status?.[svc.key] === "ok" ? "#22c55e" : checking ? "#f59e0b" : "#ef4444",
                }}>
                  {checking ? "checking" : (status?.[svc.key] ?? "unknown")}
                </span>
              </div>
            ))}
          </div>
          {status?.checkedAt && (
            <div style={{ fontSize: 10, color: "#3a4060", marginTop: 12 }}>
              Last checked: {new Date(status.checkedAt).toLocaleTimeString()}
            </div>
          )}
        </div>

        {/* App Info */}
        <div className="hd-settings-card">
          <div style={{ fontSize: 11, fontWeight: 700, letterSpacing: "0.08em", textTransform: "uppercase", color: "#5a6280", marginBottom: 12 }}>About</div>
          <div className="hd-about-grid">
            {([
              ["App",      "HelpdeskAI"],
              ["Frontend", "Next.js 16 + CopilotKit 1.54"],
              ["Agent",    agentMode === "v2"
                ? "MAF Workflow (Orchestrator + 4 Specialists)"
                : "Microsoft Agents Framework (Single Agent)"],
              ["AI Model", agentMode === "v2"
                ? "Azure OpenAI (gpt-5.2-chat)"
                : "Azure OpenAI (gpt-5.3-chat)"],
              ["Route",    agentMode === "v2" ? "/agent/v2" : "/agent"],
              ["Search",   "Azure AI Search (Basic tier)"],
            ] as [string, string][]).map(([label, value]) => (
              <div key={label} className="hd-kv-row">
                <span className="hd-kv-key">{label}</span>
                <span className="hd-kv-value">{value}</span>
              </div>
            ))}
          </div>
        </div>

        {/* Model Requirements */}
        <div className="hd-settings-card hd-settings-card--accent">
          <div style={{ fontSize: 11, fontWeight: 700, letterSpacing: "0.08em", textTransform: "uppercase", color: "#3d5afe", marginBottom: 12 }}>
            Model Requirements
          </div>
          <div style={{ fontSize: 12, color: "#9098b0", lineHeight: 1.7 }}>
            HelpdeskAI requires a model that supports <span style={{ color: "#e8eaf0" }}>OpenAI-compatible tool calling</span> and reliably follows multi-step agentic instructions.
          </div>
          <div style={{ marginTop: 12, display: "flex", flexDirection: "column", gap: 6 }}>
            {([
              ["✅ Current v1 baseline", "gpt-5.3-chat"],
              ["✅ Current v2 baseline", "gpt-5.2-chat (multi-agent orchestration)"],
              ["⚠️  Model changes", "Treat any switch away from the standard pair as a regression event"],
              ["❌ Not compatible", "Models without tool calling or agentic instruction following"],
            ] as [string, string][]).map(([label, value]) => (
              <div key={label} className="hd-model-row">
                <span className="hd-model-key">{label}</span>
                <span className="hd-model-value">{value}</span>
              </div>
            ))}
          </div>
          <div style={{ marginTop: 12, fontSize: 11, color: "#5a6280" }}>
            See <span style={{ color: "#3d5afe", fontFamily: "monospace" }}>docs/model-compatibility.md</span> for full technical details.
          </div>
        </div>

      </div>
    </div>
  );
}

// ── Attachment context ────────────────────────────────────────────────────────────
interface AttachmentContextValue {
  attachedFiles: AttachedFile[];
  onAdd: (file: File) => void;
  onRemove: (name: string) => void;
  onRetry: (name: string) => void;
  clearAll: () => void;
  onSendStarted: () => void;
  onResponseComplete: () => void;
  onResponseReset: () => void;
}
const AttachmentContext = createContext<AttachmentContextValue>({
  attachedFiles: [], onAdd: () => {}, onRemove: () => {}, onRetry: () => {}, clearAll: () => {}, onSendStarted: () => {}, onResponseComplete: () => {}, onResponseReset: () => {},
});

// ── Custom chat input with built-in paperclip ────────────────────────────────────
function CustomChatInput({ inProgress, onSend, onStop }: InputProps) {
  const { attachedFiles, onAdd, onRemove, onRetry, clearAll, onSendStarted, onResponseComplete, onResponseReset } = useContext(AttachmentContext);
  const [text, setText] = useState("");
  // Drives textarea overflow via React state so reconciliation never fights the value.
  const [textOverflow, setTextOverflow] = useState<"hidden" | "auto">("hidden");
  const fileRef = useRef<HTMLInputElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const prevInProgress = useRef(false);

  // Stable refs for callbacks — avoids re-running the effect when callbacks are recreated.
  const onResponseCompleteRef = useRef(onResponseComplete);
  const onResponseResetRef    = useRef(onResponseReset);
  onResponseCompleteRef.current = onResponseComplete;
  onResponseResetRef.current    = onResponseReset;

  // Detect inProgress transitions to signal parent.
  useEffect(() => {
    if (prevInProgress.current && !inProgress) {
      onResponseCompleteRef.current();
    } else if (!prevInProgress.current && inProgress) {
      onResponseResetRef.current();
    }
    prevInProgress.current = inProgress;
  }, [inProgress]);

  const isUploading = attachedFiles.some(f => f.uploading);
  const readyFiles = attachedFiles.filter(f => !f.uploading && !f.error);
  // Disable input and attachment while agent is responding or uploading
  const inputDisabled = inProgress || isUploading;
  const canSend = !inputDisabled && (text.trim().length > 0 || readyFiles.length > 0);

  const fileIcon = (name: string) => {
    const e = name.split(".").pop()?.toLowerCase();
    if (e === "pdf")  return "📕";
    if (e === "docx") return "📝";
    if (e === "png" || e === "jpg" || e === "jpeg") return "🖼️";
    return "📄";
  };

  const handleSend = async () => {
    const msg = text.trim() || (readyFiles.length > 0 ? "Please read and summarize the attached file." : "");
    if (!msg) return;
    onSendStarted();
    setText("");
    if (textareaRef.current) textareaRef.current.style.height = "auto";
    await onSend(msg);
    clearAll();
  };

  const handleKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      if (canSend) handleSend();
    }
  };

  const handleTextChange = (e: ChangeEvent<HTMLTextAreaElement>) => {
    setText(e.target.value);
    const ta = e.target;
    ta.style.height = "auto";
    const scrollH = ta.scrollHeight;
    ta.style.height = `${Math.min(scrollH, 160)}px`;
    setTextOverflow(scrollH > 160 ? "auto" : "hidden");
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) onAdd(file);
    e.target.value = "";
  };

  return (
    <div className="copilotKitInputContainer">
      <div className="copilotKitInput">
        {/* Attachment chips — inside the input box, above the textarea */}
        {attachedFiles.length > 0 && (
          <div style={{ display: "flex", flexWrap: "wrap", gap: 5, marginBottom: 8 }}>
            {attachedFiles.map(f => {
              const color = f.error ? "#ef4444" : f.uploading ? "#3d5afe" : "#22c55e";
              return (
                <span key={f.name} style={{
                  display: "inline-flex", alignItems: "center", gap: 4,
                  fontSize: 11, borderRadius: 4, padding: "2px 8px",
                  background: `${color}22`, color, border: `1px solid ${color}44`,
                }}>
                  {f.uploading ? "⏳" : f.error ? "⚠️" : fileIcon(f.name)} {f.name}
                  {f.error && (
                    <button onClick={() => onRetry(f.name)} title="Retry upload" style={{
                      background: "none", border: "none", cursor: "pointer",
                      color, fontSize: 14, lineHeight: 1, padding: "0 2px", marginLeft: 2,
                    }}>↺</button>
                  )}
                  {!f.uploading && (
                    <button onClick={() => onRemove(f.name)} style={{
                      background: "none", border: "none", cursor: "pointer",
                      color, fontSize: 13, lineHeight: 1, padding: "0 2px", marginLeft: 2,
                    }}>×</button>
                  )}
                </span>
              );
            })}
          </div>
        )}

        {/* Textarea */}
        <textarea
          ref={textareaRef}
          placeholder={inputDisabled ? (isUploading ? "Waiting for file upload to complete…" : "Agent is responding…") : "Describe your IT issue…"}
          value={text}
          onChange={handleTextChange}
          onKeyDown={handleKeyDown}
          disabled={inputDisabled}
          rows={1}
          style={{ overflowY: textOverflow, opacity: inputDisabled ? 0.5 : 1 }}
        />

        {/* Controls: paperclip ← spacer → send/stop */}
        <div className="copilotKitInputControls">
          <input ref={fileRef} type="file"
            accept=".txt,.pdf,.docx,.png,.jpg,.jpeg,text/plain,application/pdf,application/vnd.openxmlformats-officedocument.wordprocessingml.document,image/png,image/jpeg"
            style={{ display: "none" }} onChange={handleFileChange} />
          <button
            className="copilotKitInputControlButton"
            onClick={() => fileRef.current?.click()}
            disabled={inputDisabled}
            title="Attach a file (.txt, .pdf, .docx, .png, .jpg)"
            aria-label="Attach file"
          >
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"
                 strokeLinecap="round" strokeLinejoin="round" width="16" height="16">
              <path d="m21.44 11.05-9.19 9.19a6 6 0 0 1-8.49-8.49l8.57-8.57A4 4 0 1 1 18 8.84l-8.59 8.57a2 2 0 0 1-2.83-2.83l8.49-8.48"/>
            </svg>
          </button>

          <div style={{ flexGrow: 1 }} />

          <button
            className="copilotKitInputControlButton"
            onClick={inProgress ? onStop : handleSend}
            disabled={!inProgress && !canSend}
            aria-label={inProgress ? "Stop" : "Send"}
          >
            {inProgress ? (
              <svg viewBox="0 0 24 24" fill="currentColor" width="16" height="16">
                <rect x="4" y="4" width="16" height="16" rx="2"/>
              </svg>
            ) : (
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"
                   strokeLinecap="round" strokeLinejoin="round" width="16" height="16">
                <path d="M22 2 11 13"/><path d="m22 2-7 20-4-9-9-4 20-7z"/>
              </svg>
            )}
          </button>
        </div>
      </div>
    </div>
  );
}


// ── Evaluations page ─────────────────────────────────────────────────────────
interface EvalMetric   { name: string; rating: string; reason: string; }
interface EvalScenario { scenarioName: string; message: string; passed: boolean; primaryEvaluator: string; metrics: EvalMetric[]; createdAt: string; agentResponse?: string; }
interface EvalSummary  { executionName: string; total: number; passed: number; failed: number; isComplete: boolean; isRunning: boolean; completedAt: string | null; }

function ratingColor(rating: string): string {
  if (rating === "Good" || rating === "Exceptional") return "#22c55e";
  if (rating === "Poor" || rating === "Unacceptable") return "#ef4444";
  return "#9098b0";
}

function EvalPage() {
  const [summaries, setSummaries]         = React.useState<EvalSummary[]>([]);
  const [selected, setSelected]           = React.useState<string | null>(null);
  const [scenarios, setScenarios]         = React.useState<EvalScenario[]>([]);
  const [loadingList, setLoadingList]     = React.useState(true);
  const [loadingDetail, setLoadingDetail] = React.useState(false);
  const [running, setRunning]             = React.useState(false);
  const [runExec, setRunExec]             = React.useState<string | null>(null);
  const [error, setError]                 = React.useState<string | null>(null);

  const fetchSummaries = React.useCallback(async () => {
    try {
      const res = await fetch("/api/eval-results", { cache: "no-store" });
      if (!res.ok) throw new Error(`${res.status}`);
      setSummaries(await res.json());
      setError(null);
    } catch (e) {
      setError(`Failed to load eval results: ${e}`);
    } finally {
      setLoadingList(false);
    }
  }, []);

  // Initial load
  React.useEffect(() => { void fetchSummaries(); }, [fetchSummaries]);

  // Auto-refresh while a run is in progress
  React.useEffect(() => {
    const anyRunning = running || summaries.some(s => s.isRunning);
    if (!anyRunning) return;
    const timer = setInterval(() => { void fetchSummaries(); }, 8000);
    return () => clearInterval(timer);
  }, [running, summaries, fetchSummaries]);

  // Stop "running" indicator once the new execution appears and is complete
  React.useEffect(() => {
    if (!runExec) return;
    const exec = summaries.find(s => s.executionName === runExec);
    if (exec?.isComplete) { setRunning(false); setRunExec(null); }
  }, [summaries, runExec]);

  const handleRunEvals = async () => {
    setRunning(true);
    setError(null);
    try {
      const res = await fetch("/api/eval-results", { method: "POST", cache: "no-store" });
      if (!res.ok) throw new Error(`${res.status}`);
      const body = await res.json() as { executionName?: string };
      setRunExec(body.executionName ?? null);
      void fetchSummaries();
    } catch (e) {
      setError(`Failed to start eval run: ${e}`);
      setRunning(false);
    }
  };

  const handleSelectExecution = async (execName: string) => {
    if (selected === execName) { setSelected(null); setScenarios([]); return; }
    setSelected(execName);
    setLoadingDetail(true);
    try {
      const res = await fetch(`/api/eval-results?run=${encodeURIComponent(execName)}`, { cache: "no-store" });
      if (!res.ok) throw new Error(`${res.status}`);
      setScenarios(await res.json());
    } catch (e) {
      setError(`Failed to load run details: ${e}`);
    } finally {
      setLoadingDetail(false);
    }
  };

  const passRate = (s: EvalSummary) =>
    s.total > 0 ? Math.round((s.passed / s.total) * 100) : 0;

  return (
    <div className="hd-page-scroll">
      <div className="hd-page-stack" style={{ maxWidth: 860 }}>
        {/* ── Header ── */}
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", flexWrap: "wrap", gap: 12 }}>
          <div>
            <div style={{ fontSize: 13, fontWeight: 700, letterSpacing: "0.08em", textTransform: "uppercase", color: "#5a6280", marginBottom: 4 }}>Agent Quality</div>
            <div style={{ fontSize: 20, fontWeight: 700, color: "#e8eaf0" }}>Evaluations</div>
          </div>
          <button
            onClick={() => { void handleRunEvals(); }}
            disabled={running}
            style={{
              display: "flex", alignItems: "center", gap: 8,
              background: running ? "#1c2030" : "#3d5afe",
              color: running ? "#5a6280" : "#fff",
              border: "1px solid " + (running ? "#ffffff12" : "#3d5afe"),
              borderRadius: 8, padding: "9px 18px",
              fontSize: 13, fontWeight: 600, cursor: running ? "not-allowed" : "pointer",
              transition: "all 0.15s",
            }}
          >
            {running ? (
              <>
                <span style={{ display: "inline-block", width: 12, height: 12, border: "2px solid #5a6280", borderTopColor: "#9098b0", borderRadius: "50%", animation: "spin 0.8s linear infinite" }} />
                Running scenarios…
              </>
            ) : "▶ Run Evals"}
          </button>
        </div>

        {error && (
          <div style={{ background: "#ef444412", border: "1px solid #ef444430", borderRadius: 8, padding: "10px 14px", fontSize: 13, color: "#ef4444" }}>
            {error}
          </div>
        )}

        {/* ── Execution list ── */}
        {loadingList ? (
          <div style={{ color: "#5a6280", fontSize: 13, padding: "24px 0" }}>Loading results…</div>
        ) : summaries.length === 0 ? (
          <div className="hd-settings-card" style={{ textAlign: "center", padding: "40px 24px", color: "#5a6280", fontSize: 13 }}>
            No eval runs yet. Click <strong style={{ color: "#9098b0" }}>▶ Run Evals</strong> to start the first run.
          </div>
        ) : summaries.map(s => (
          <div key={s.executionName} className="hd-settings-card" style={{ padding: 0, overflow: "hidden" }}>
            {/* Execution summary row */}
            <button
              onClick={() => { void handleSelectExecution(s.executionName); }}
              style={{
                width: "100%", display: "flex", alignItems: "center", gap: 16,
                padding: "16px 20px", background: "none", border: "none",
                cursor: "pointer", textAlign: "left",
              }}
            >
              {/* Pass rate badge */}
              <div style={{
                flexShrink: 0, width: 52, height: 52, borderRadius: "50%",
                background: `conic-gradient(${passRate(s) === 100 ? "#22c55e" : passRate(s) >= 70 ? "#f59e0b" : "#ef4444"} ${passRate(s) * 3.6}deg, #ffffff0a 0deg)`,
                display: "flex", alignItems: "center", justifyContent: "center",
                fontSize: 13, fontWeight: 700,
                color: passRate(s) === 100 ? "#22c55e" : passRate(s) >= 70 ? "#f59e0b" : "#ef4444",
              }}>
                <div style={{ width: 40, height: 40, borderRadius: "50%", background: "#0f1117", display: "flex", alignItems: "center", justifyContent: "center" }}>
                  {passRate(s)}%
                </div>
              </div>

              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 13, fontWeight: 600, color: "#e8eaf0", marginBottom: 3 }}>
                  {s.executionName.replace(/_/g, " ")}
                </div>
                <div style={{ fontSize: 11, color: "#5a6280" }}>
                  {s.isRunning ? (
                    <span style={{ color: "#f59e0b" }}>● Running — {s.total} complete</span>
                  ) : (
                    <span>{s.passed} passed · {s.failed} failed · {s.total} scenarios</span>
                  )}
                </div>
              </div>

              <span style={{ fontSize: 12, color: "#5a6280", flexShrink: 0, transform: selected === s.executionName ? "rotate(90deg)" : "rotate(0)", transition: "transform 0.2s", display: "inline-block" }}>▶</span>
            </button>

            {/* Expanded scenario table */}
            {selected === s.executionName && (
              <div style={{ borderTop: "1px solid #ffffff0c", padding: "12px 20px 16px" }}>
                {loadingDetail ? (
                  <div style={{ fontSize: 13, color: "#5a6280", padding: "8px 0" }}>Loading scenarios…</div>
                ) : (
                  <div style={{ overflowX: "auto", WebkitOverflowScrolling: "touch" }}>
                  <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12, minWidth: 480 }}>
                    <thead>
                      <tr style={{ color: "#5a6280", textAlign: "left" }}>
                        <th style={{ padding: "4px 8px 8px 0", fontWeight: 600 }}>Scenario</th>
                        <th className="hd-eval-col-primary" style={{ padding: "4px 8px 8px", fontWeight: 600 }}>Primary</th>
                        <th style={{ padding: "4px 8px 8px", fontWeight: 600 }}>Result</th>
                        <th style={{ padding: "4px 0 8px 8px", fontWeight: 600 }}>Metrics</th>
                      </tr>
                    </thead>
                    <tbody>
                      {scenarios.map(sc => (
                        <tr key={sc.scenarioName} style={{ borderTop: "1px solid #ffffff06" }}>
                          <td style={{ padding: "8px 8px 8px 0", color: "#9098b0", maxWidth: 200, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                            {(() => {
                              const isV2 = sc.scenarioName.includes("_V2_");
                              const label = sc.scenarioName
                                .replace(/^Test\d+_V2_/, "")
                                .replace(/^Test\d+_/, "")
                                .replace(/_/g, " ");
                              return (
                                <>
                                  <span style={{
                                    padding: "1px 5px", borderRadius: 3, fontSize: 9, fontWeight: 700,
                                    background: isV2 ? "#7c3aed18" : "#1d4ed818",
                                    color: isV2 ? "#a78bfa" : "#60a5fa",
                                    marginRight: 5, flexShrink: 0,
                                  }}>{isV2 ? "V2" : "V1"}</span>
                                  {label}
                                </>
                              );
                            })()}
                          </td>
                          <td className="hd-eval-col-primary" style={{ padding: "8px", color: "#5a6280" }}>{sc.primaryEvaluator}</td>
                          <td style={{ padding: "8px" }}>
                            <span style={{
                              padding: "2px 8px", borderRadius: 4, fontSize: 11, fontWeight: 700,
                              background: sc.passed ? "#22c55e18" : "#ef444418",
                              color: sc.passed ? "#22c55e" : "#ef4444",
                            }}>
                              {sc.passed ? "PASS" : "FAIL"}
                            </span>
                          </td>
                          <td style={{ padding: "8px 0 8px 8px" }}>
                            <div style={{ display: "flex", gap: 4, flexWrap: "wrap" }}>
                              {sc.metrics.map(m => (
                                <span key={m.name} title={`${m.name}: ${m.rating}${m.reason ? ` — ${m.reason}` : ""}`}
                                  style={{ padding: "1px 6px", borderRadius: 4, fontSize: 10, fontWeight: 700, background: ratingColor(m.rating) + "18", color: ratingColor(m.rating), cursor: "help" }}>
                                  {m.name.replace("Evaluator", "").replace("Evaluation", "")}
                                </span>
                              ))}
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  </div>
                )}
              </div>
            )}
          </div>
        ))}
      </div>
      <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
    </div>
  );
}

// ── Citation markdown renderers ───────────────────────────────────────────────
// Processes [KB-xxx] patterns in agent markdown and renders them as styled badges.
function processCitationChildren(children: React.ReactNode): React.ReactNode {
  if (!children) return children;
  if (typeof children === "string") {
    const regex = new RegExp(KB_CITATION_REGEX.source, "g");
    if (!regex.test(children)) return children;
    // Reset regex after test
    regex.lastIndex = 0;
    const parts: React.ReactNode[] = [];
    let lastIndex = 0;
    let match: RegExpExecArray | null;
    while ((match = regex.exec(children)) !== null) {
      if (match.index > lastIndex) parts.push(children.slice(lastIndex, match.index));
      parts.push(<CitationBadge key={`c-${match.index}`} id={match[1]} />);
      lastIndex = regex.lastIndex;
    }
    if (lastIndex < children.length) parts.push(children.slice(lastIndex));
    return <>{parts}</>;
  }
  if (Array.isArray(children)) return children.map((c, i) => <React.Fragment key={i}>{processCitationChildren(c)}</React.Fragment>);
  return children;
}

const citationTagRenderers = {
  p: ({ children, ...props }: { children?: React.ReactNode; [k: string]: unknown }) => (
    <p {...(props as React.HTMLAttributes<HTMLParagraphElement>)}>{processCitationChildren(children)}</p>
  ),
  li: ({ children, ...props }: { children?: React.ReactNode; [k: string]: unknown }) => (
    <li {...(props as React.LiHTMLAttributes<HTMLLIElement>)}>{processCitationChildren(children)}</li>
  ),
};

export function HelpdeskChat({ currentUser }: { currentUser: CurrentUser }) {
  const [page, setPage]       = useState<Page>("chat");
  const [tickets, setTickets]  = useState<Ticket[]>([]);
  const [refreshKey, setRefreshKey] = useState(0);
  const [attachedFiles, setAttachedFiles] = useState<AttachedFile[]>([]);
  const [incidentBannerVisible] = useState<boolean>(() => {
    if (typeof window === "undefined") return true;
    return localStorage.getItem("incident-banner") !== "hidden";
  });
  const [activeIncidents, setActiveIncidents] = useState<ActiveIncidentSummary[]>([]);
  const [incidentFeedStatus, setIncidentFeedStatus] = useState<"loading" | "ok" | "empty" | "error">("loading");
  const [incidentBannerExpanded, setIncidentBannerExpanded] = useState(false);
  const current = NAV_ITEMS.find(n => n.id === page)!;

  const firstName = currentUser.name.split(" ")[0];
  const defaultGreeting = `Hi ${firstName}! What IT issue can I help you with today?`;
  const [chatInitial, setChatInitial] = useState(() => consumePendingChatInitial() ?? defaultGreeting);
  useCopilotChat();

  useEffect(() => {
    if (chatInitial === defaultGreeting) return;
    const timeoutId = window.setTimeout(() => setChatInitial(defaultGreeting), 60_000);
    return () => window.clearTimeout(timeoutId);
  }, [chatInitial, defaultGreeting]);

  const handleCopilotError = useCallback((event: unknown) => {
    // Extract message from any shape CopilotKit may pass: Error, { message }, { error }, string.
    const msg = String(
      event instanceof Error ? event.message
      : event && typeof event === "object" && "message" in event ? (event as any).message
      : event && typeof event === "object" && "error" in event ? (event as any).error
      : event
    );
    if (msg.includes("unknown action:") || msg.includes("SharedStorage") || msg.includes("WebSocketConnection")) return;
    if (msg.includes(CONTENT_SAFETY_MARKER)) {
      waitingRef.current = false;
      setLastStats(null);
      setAttachedFiles([]);
      window.sessionStorage.setItem(CONTENT_SAFETY_FLASH_KEY, CONTENT_SAFETY_MESSAGE);
      window.dispatchEvent(new Event(CHAT_RESET_EVENT));
      return;
    }
    console.error("[CopilotKit error]", event);
  }, []);

  const { threadId } = useCopilotContext();
  const threadIdRef = useRef(threadId);
  threadIdRef.current = threadId;
  const sendTimeRef = useRef<number>(0);
  const waitingRef = useRef(false);
  const [lastStats, setLastStats] = useState<{ elapsedMs: number; promptTokens: number; completionTokens: number } | null>(null);
  const chatWrapperRef = useRef<HTMLDivElement>(null);
  const shouldStickToBottomRef = useRef(true);

  // Use a ref so onResponseComplete (captured with [] deps) always calls the latest closure.
  // fetchStatsRef.current is reassigned each render to capture the latest refs/setters.
  const fetchStatsRef = useRef<(retryDelay?: number) => void>(null!);
  fetchStatsRef.current = (retryDelay = 1000) => {
    const tid = threadIdRef.current;
    if (!waitingRef.current) return;
    const url = tid
      ? `/api/copilotkit/usage?threadId=${encodeURIComponent(tid)}`
      : `/api/copilotkit/usage`;
    // Retry on both null data (backend not ready yet) and network errors.
    const scheduleRetry = () => {
      if (waitingRef.current && retryDelay <= 4000)
        setTimeout(() => fetchStatsRef.current(retryDelay * 2), retryDelay);
    };
    fetch(url)
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        if (data?.promptTokens) {
          setLastStats({ elapsedMs: Date.now() - sendTimeRef.current, ...data });
          waitingRef.current = false;
        } else {
          scheduleRetry();
        }
      })
      .catch(scheduleRetry);
  };


  const handleTicketCreated = useCallback((ticket: Ticket) => {
    setTickets(prev => [ticket, ...prev]);
    setRefreshKey(k => k + 1);
  }, []);

  const handleAttachFile = useCallback(async (file: File) => {
    const placeholder: AttachedFile = {
      name: file.name,
      contentType: file.type || "text/plain",
      blobUrl: "",
      uploading: true,
      file,
    };
    setAttachedFiles(prev => [...prev, placeholder]);

    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 30_000);

    try {
      const body = new FormData();
      body.append("file", file);
      // Send thread ID so the upload is keyed to this conversation, not a shared session.
      const headers: Record<string, string> = {};
      if (threadIdRef.current) headers["X-Session-Id"] = threadIdRef.current;
      const res = await fetch("/api/upload", { method: "POST", headers, body, signal: controller.signal });
      clearTimeout(timeoutId);
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || `HTTP ${res.status}`);
      }
      const data = await res.json() as { fileName: string; blobUrl: string; contentType: string };
      setAttachedFiles(prev =>
        prev.map(f =>
          f.name === placeholder.name && f.uploading
            ? { name: data.fileName, contentType: data.contentType, blobUrl: data.blobUrl, uploading: false }
            : f
        )
      );
    } catch (err) {
      clearTimeout(timeoutId);
      const isTimeout = err instanceof DOMException && err.name === "AbortError";
      setAttachedFiles(prev =>
        prev.map(f =>
          f.name === placeholder.name && f.uploading
            ? { ...f, uploading: false, error: isTimeout ? "Upload timed out" : (err as Error).message }
            : f
        )
      );
    }
  }, []);

  const handleRetryAttachment = useCallback((name: string) => {
    setAttachedFiles(prev => {
      const entry = prev.find(f => f.name === name);
      if (!entry?.file) return prev;
      const fileToRetry = entry.file;
      // Schedule the re-upload outside of setState to avoid nested updates.
      setTimeout(() => handleAttachFile(fileToRetry), 0);
      return prev.filter(f => f.name !== name);
    });
  }, [handleAttachFile]);

  const handleRemoveAttachment = useCallback((name: string) => {
    setAttachedFiles(prev => prev.filter(f => f.name !== name));
  }, []);

  const clearAllAttachments = useCallback(() => setAttachedFiles([]), []);

  // Memoize callbacks before creating the context value below.
  const onSendStarted = useCallback(() => {
    sendTimeRef.current = Date.now();
    setLastStats(null);
    waitingRef.current = true;
  }, []);

  const onResponseComplete = useCallback(() => {
    waitingRef.current = true;  // re-arm before polling — a previous fetchStats round may have set it false
    fetchStatsRef.current(200);
  }, []);  // fetchStatsRef is stable — no deps needed

  const onResponseReset = useCallback(() => {
    setLastStats(null);
    waitingRef.current = true;
  }, []);

  // ── Stats chip (rendered in header) ────────────────────────────────────────────
  // Pure React state — set after each agent response, cleared on next send.

  // Stable context value — only re-created when attachedFiles list changes.
  const attachmentContextValue = useMemo(() => ({
    attachedFiles,
    onAdd: handleAttachFile,
    onRemove: handleRemoveAttachment,
    onRetry: handleRetryAttachment,
    clearAll: clearAllAttachments,
    onSendStarted,
    onResponseComplete,
    onResponseReset,
  }), [attachedFiles, handleAttachFile, handleRemoveAttachment, handleRetryAttachment,
      clearAllAttachments, onSendStarted, onResponseComplete, onResponseReset]);

  useEffect(() => {
    if (page !== "chat") return;

    const wrapper = chatWrapperRef.current;
    const scrollHost = wrapper?.querySelector(":scope > div > div:first-child") as HTMLDivElement | null;
    if (!scrollHost) return;

    const nearBottom = () =>
      scrollHost.scrollHeight - scrollHost.scrollTop - scrollHost.clientHeight < 80;

    const syncStickiness = () => {
      shouldStickToBottomRef.current = nearBottom();
    };

    const scrollToBottom = () => {
      if (!shouldStickToBottomRef.current) return;
      scrollHost.scrollTo({ top: scrollHost.scrollHeight, behavior: "auto" });
    };

    syncStickiness();
    scrollToBottom();

    const observer = new MutationObserver(() => {
      scrollToBottom();
    });
    observer.observe(scrollHost, { childList: true, subtree: true, characterData: true });

    scrollHost.addEventListener("scroll", syncStickiness, { passive: true });

    return () => {
      observer.disconnect();
      scrollHost.removeEventListener("scroll", syncStickiness);
    };
  }, [page, threadId]);

  useEffect(() => {
    if (!incidentBannerVisible) {
      setActiveIncidents([]);
      setIncidentFeedStatus("empty");
      setIncidentBannerExpanded(false);
      return;
    }

    let cancelled = false;
    setIncidentFeedStatus("loading");
    fetch("/api/incidents", { cache: "no-store" })
      .then(async r => {
        const data = await r.json().catch(() => null);
        if (!r.ok) {
          const message = typeof data?.error === "string" ? data.error : `HTTP ${r.status}`;
          throw new Error(message);
        }
        return data as { incidents?: ActiveIncidentSummary[] };
      })
      .then((data: { incidents?: ActiveIncidentSummary[] }) => {
        if (!cancelled) {
          const incidents = Array.isArray(data.incidents) ? data.incidents : [];
          setActiveIncidents(incidents);
          setIncidentFeedStatus(incidents.length > 0 ? "ok" : "empty");
          if (incidents.length === 0) setIncidentBannerExpanded(false);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setActiveIncidents([]);
          setIncidentFeedStatus("error");
          setIncidentBannerExpanded(false);
        }
      });

    return () => { cancelled = true; };
  }, [incidentBannerVisible]);

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
          <button
            onClick={() => signOut({ callbackUrl: "/signed-out" })}
            style={{
              width: "100%",
              marginBottom: 10,
              border: "1px solid rgba(255,255,255,0.12)",
              background: "transparent",
              color: "#9098b0",
              borderRadius: 8,
              padding: "8px 10px",
              fontSize: 12,
              cursor: "pointer",
              textAlign: "left",
            }}
          >
            Sign out
          </button>
          <div className="hd-powered">Powered by <strong>Microsoft Agents</strong></div>
        </div>
      </aside>

      <main className="hd-main">
        <header className="hd-header">
          <div>
            <h1 className="hd-title">{current.label}</h1>
            <p className="hd-subtitle">{current.subtitle}</p>
          </div>
          {/* Stats chip — lives in the header row so it never floats over chat content */}
          {lastStats && page === "chat" && (
            <div
              style={{
                marginLeft: "auto",
                fontSize: 11,
                color: "#9098b0",
                fontFamily: "monospace",
                letterSpacing: "0.02em",
                whiteSpace: "nowrap",
                background: "rgba(255,255,255,0.05)",
                border: "1px solid rgba(255,255,255,0.1)",
                borderRadius: 6,
                padding: "4px 12px",
                pointerEvents: "none",
                flexShrink: 0,
              }}
            >
              ⏱ {(lastStats.elapsedMs / 1000).toFixed(1)}s &nbsp;·&nbsp; 📥 {lastStats.promptTokens} in / 📤 {lastStats.completionTokens} out
            </div>
          )}
        </header>

        {incidentBannerVisible && page !== "settings" && activeIncidents.length > 0 && (
          <div style={{
            margin: "0 var(--page-gutter) 12px",
            padding: "10px 14px",
            borderRadius: 10,
            border: "1px solid #f59e0b33",
            background: "linear-gradient(180deg, rgba(245, 158, 11, 0.12), rgba(245, 158, 11, 0.05))",
            display: "flex",
            flexDirection: "column",
            gap: 10,
          }}>
            <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
              <span style={{ fontSize: 15 }}>⚠️</span>
              <span style={{ fontSize: 12, fontWeight: 700, letterSpacing: "0.06em", textTransform: "uppercase", color: "#f59e0b" }}>
                Active Incident Monitor
              </span>
              <span style={{ fontSize: 12, color: "#9098b0" }}>
                {activeIncidents.length} active issue{activeIncidents.length === 1 ? "" : "s"} detected
              </span>
              <button
                type="button"
                onClick={() => setIncidentBannerExpanded(prev => !prev)}
                style={{
                  marginLeft: "auto",
                  border: "1px solid #f59e0b33",
                  background: "rgba(255,255,255,0.04)",
                  color: "#f8d08b",
                  borderRadius: 999,
                  padding: "4px 10px",
                  fontSize: 11,
                  fontWeight: 600,
                  cursor: "pointer",
                }}
                aria-expanded={incidentBannerExpanded}
                aria-label={incidentBannerExpanded ? "Hide active incident details" : "Show active incident details"}
              >
                {incidentBannerExpanded ? "Hide details" : "View details"}
              </button>
            </div>
            {incidentBannerExpanded && (
              <div style={{ display: "flex", flexDirection: "column", gap: 6, maxHeight: 180, overflowY: "auto", paddingRight: 2 }}>
                {activeIncidents.map(incident => (
                  <div
                    key={`${incident.incidentId ?? incident.service}`}
                    style={{
                      fontSize: 12,
                      color: "#d7dcec",
                      lineHeight: 1.5,
                      background: "rgba(255,255,255,0.03)",
                      border: "1px solid rgba(255,255,255,0.06)",
                      borderRadius: 8,
                      padding: "8px 10px",
                    }}
                  >
                    <strong>{incident.service}</strong>
                    {incident.incidentId ? ` (${incident.incidentId})` : ""}: {incident.message}
                    {incident.eta ? ` ETA ${incident.eta}.` : ""}
                  </div>
                ))}
              </div>
            )}
          </div>
        )}

        {incidentBannerVisible && page !== "settings" && incidentFeedStatus === "error" && (
          <div style={{
            margin: "0 var(--page-gutter) 12px",
            padding: "10px 14px",
            borderRadius: 10,
            border: "1px solid #ef444433",
            background: "rgba(239, 68, 68, 0.08)",
            fontSize: 12,
            color: "#fca5a5",
          }}>
            Live Incident Monitor is enabled, but the incident feed is currently unavailable.
          </div>
        )}

        {incidentBannerVisible && page !== "settings" && incidentFeedStatus === "empty" && (
          <div style={{
            margin: "0 var(--page-gutter) 12px",
            padding: "10px 14px",
            borderRadius: 10,
            border: "1px solid #ffffff12",
            background: "rgba(255,255,255,0.04)",
            fontSize: 12,
            color: "#9098b0",
          }}>
            Live Incident Monitor is enabled. No active incidents are currently being returned.
          </div>
        )}

        {page === "chat" && (
          <AttachmentContext.Provider value={attachmentContextValue}>
              <HelpdeskActions
                tickets={tickets}
                onTicketCreated={handleTicketCreated}
                attachedFiles={attachedFiles}
                currentUser={currentUser}
              />

              <div className="hd-chat-wrapper" style={ckTheme} ref={chatWrapperRef}>
                <CopilotChat
                  Input={CustomChatInput}
                  markdownTagRenderers={citationTagRenderers}
                  onError={handleCopilotError}
                  labels={{
                    title: "IT Support",
                    initial: chatInitial,
                    placeholder: "Describe your IT issue…",
                  }}
                />
              </div>
          </AttachmentContext.Provider>
        )}

        {page === "tickets"  && <TicketsPage agentTickets={tickets} refreshKey={refreshKey} currentUser={currentUser} />}
        {page === "kb"       && <KbPage />}
        {page === "settings" && <SettingsPage currentUser={currentUser} />}
        {page === "eval"     && <EvalPage />}
      </main>
    </div>
  );
}
