"use client";

import { createContext, useContext, useEffect, useRef, useState, type KeyboardEvent, type ChangeEvent } from "react";
import { CopilotChat, CopilotKitCSSProperties, type InputProps } from "@copilotkit/react-ui";
import { useCopilotContext } from "@copilotkit/react-core";
import "@copilotkit/react-ui/styles.css";
import { HelpdeskActions, Ticket } from "./HelpdeskActions";
import type { AttachedFile } from "./AttachmentBar";
import { DEMO_USER, PRIORITY_COLOR, CATEGORY_ICON, KB_CATEGORY_COLOR } from "../lib/constants";

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

interface ServerTicket {
  id: string; title: string; description: string;
  status: string; priority: string; category: string;
  requestedBy: string; assignedTo?: string;
  createdAt: string; updatedAt: string; resolution?: string;
}

interface KbArticle {
  id: string; title: string; content: string; category?: string;
}

const STATUS_COLOR: Record<string, string> = {
  Open: "#3d5afe", InProgress: "#f59e0b", PendingUser: "#f97316",
  Resolved: "#22c55e", Closed: "#5a6280",
};
const STATUS_LABEL: Record<string, string> = {
  Open: "Open", InProgress: "In Progress", PendingUser: "Pending",
  Resolved: "Resolved", Closed: "Closed",
};

function TicketsPage({ agentTickets, refreshKey }: { agentTickets: Ticket[]; refreshKey: number }) {
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
    fetch(`/api/tickets?requestedBy=${encodeURIComponent(DEMO_USER)}`)
      .then(r => (r.ok ? r.json() : Promise.reject(`HTTP ${r.status}`)))
      .then((data: ServerTicket[]) => {
        if (!cancelled) { setServerTickets(data); setLoading(false); }
      })
      .catch(e => {
        if (!cancelled) { setError(String(e)); setLoading(false); }
      });
    return () => { cancelled = true; };
  }, [refreshKey]);

  const serverIds = new Set(serverTickets.map(t => t.id));
  const agentOnly: ServerTicket[] = agentTickets
    .filter(t => !serverIds.has(t.id))
    .map(t => ({
      id: t.id, title: t.title, description: t.description,
      status: t.status === "open" ? "Open" : t.status === "in_progress" ? "InProgress" : "Resolved",
      priority: t.priority.charAt(0).toUpperCase() + t.priority.slice(1),
      category: t.category,
      requestedBy: DEMO_USER,
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
    <div style={{ flex: 1, overflowY: "auto", padding: "16px 32px 24px" }}>
      {/* User context hint */}
      <div style={{ fontSize: 11, color: "#5a6280", marginBottom: 12 }}>
        Showing tickets for <span style={{ color: "#3d5afe" }}>{DEMO_USER}</span> — click any card to expand details
      </div>
      <div style={{ display: "flex", flexDirection: "column", gap: 10, maxWidth: 680 }}>
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
      <div style={{ padding: "16px 32px 8px" }}>
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
      <div style={{ flex: 1, overflowY: "auto", padding: "8px 32px 24px" }}>
        {loading ? (
          <div style={{ color: "#5a6280", fontSize: 13, paddingTop: 32, textAlign: "center" }}>Loading…</div>
        ) : error ? (
          <div style={{ color: "#ef4444", fontSize: 13, paddingTop: 32, textAlign: "center" }}>{error}</div>
        ) : articles.length === 0 ? (
          <div style={{ color: "#5a6280", fontSize: 13, paddingTop: 32, textAlign: "center" }}>
            {query.trim() ? "No articles found." : "No articles available."}
          </div>
        ) : (
          <div style={{ display: "flex", flexDirection: "column", gap: 10, maxWidth: 680 }}>
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

function SettingsPage() {
  const [status, setStatus] = useState<{ mcp: string; agent: string; checkedAt?: string } | null>(null);
  const [checking, setChecking] = useState(true);

  const check = () => {
    setChecking(true);
    fetch("/api/status", { cache: "no-store" })
      .then(r => r.json())
      .then(d => { setStatus(d); setChecking(false); })
      .catch(() => { setStatus({ mcp: "down", agent: "down" }); setChecking(false); });
  };

  useEffect(() => { check(); }, []);

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
    <div style={{ flex: 1, overflowY: "auto", padding: "24px 32px" }}>
      <div style={{ display: "flex", flexDirection: "column", gap: 20, maxWidth: 520 }}>

        {/* User Profile */}
        <div style={{
          background: "#0f1117", border: "1px solid #ffffff12",
          borderRadius: 12, padding: "20px 24px",
        }}>
          <div style={{ fontSize: 11, fontWeight: 700, letterSpacing: "0.08em", textTransform: "uppercase", color: "#5a6280", marginBottom: 16 }}>User Profile</div>
          <div style={{ display: "flex", alignItems: "center", gap: 16 }}>
            <div style={{
              width: 48, height: 48, borderRadius: "50%",
              background: "linear-gradient(135deg, #3d5afe, #6366f1)",
              display: "flex", alignItems: "center", justifyContent: "center",
              fontSize: 18, fontWeight: 700, color: "#fff", flexShrink: 0,
            }}>AJ</div>
            <div>
              <div style={{ fontSize: 15, fontWeight: 600, color: "#e8eaf0" }}>Alex Johnson</div>
              <div style={{ fontSize: 12, color: "#5a6280", marginTop: 2 }}>alex.johnson@contoso.com</div>
            </div>
          </div>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px 24px", marginTop: 16 }}>
            {([
              ["Role",       "Senior Developer"],
              ["Department", "Engineering"],
              ["Office",     "Kolkata"],
              ["Tenant",     "contoso.com"],
            ] as [string, string][]).map(([label, value]) => (
              <div key={label}>
                <div style={{ fontSize: 10, color: "#5a6280", textTransform: "uppercase", letterSpacing: "0.07em" }}>{label}</div>
                <div style={{ fontSize: 12, color: "#9098b0", marginTop: 2 }}>{value}</div>
              </div>
            ))}
          </div>
        </div>

        {/* Backend Status */}
        <div style={{
          background: "#0f1117", border: "1px solid #ffffff12",
          borderRadius: 12, padding: "20px 24px",
        }}>
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
        <div style={{
          background: "#0f1117", border: "1px solid #ffffff12",
          borderRadius: 12, padding: "20px 24px",
        }}>
          <div style={{ fontSize: 11, fontWeight: 700, letterSpacing: "0.08em", textTransform: "uppercase", color: "#5a6280", marginBottom: 12 }}>About</div>
          <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
            {([
              ["App",      "HelpdeskAI"],
              ["Frontend", "Next.js 15 + CopilotKit"],
              ["Agent",    "Microsoft Agents Framework (rc4)"],
              ["AI Model", "Azure OpenAI (GPT-4o)"],
              ["Search",   "Azure AI Search (semantic)"],
            ] as [string, string][]).map(([label, value]) => (
              <div key={label} style={{ display: "flex", gap: 12, fontSize: 12 }}>
                <span style={{ color: "#5a6280", minWidth: 72 }}>{label}</span>
                <span style={{ color: "#9098b0" }}>{value}</span>
              </div>
            ))}
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
  const fileRef = useRef<HTMLInputElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const prevInProgress = useRef(false);

  // Detect inProgress transitions to signal parent
  useEffect(() => {
    if (prevInProgress.current && !inProgress) {
      // true → false: turn complete
      onResponseComplete();
    } else if (!prevInProgress.current && inProgress) {
      // false → true: new turn starting (multi-turn) — clear stale stats and re-arm
      onResponseReset();
    }
    prevInProgress.current = inProgress;
  }, [inProgress, onResponseComplete, onResponseReset]);

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
    ta.style.height = `${Math.min(ta.scrollHeight, 160)}px`;
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
          style={{ overflow: "hidden", opacity: inputDisabled ? 0.5 : 1 }}
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

// ── Agent instructions ────────────────────────────────────────────────────────────────
const AGENT_INSTRUCTIONS = `You are an IT helpdesk assistant for Alex Johnson (Senior Developer, Engineering, Kolkata Office).
Email: ${DEMO_USER}

## MCP tools (fetch data from backend)
- get_system_status / get_active_incidents / check_impact_for_team: check IT service health
- create_ticket / get_ticket / search_tickets / update_ticket_status / add_ticket_comment: manage support tickets

## Attached documents
If the user has attached a file, it will appear as an '## Attached Document: {filename}' section in your context.
1. Always acknowledge and read any attached document before answering.
2. After reading it, call show_attachment_preview with a one-sentence summary of its contents.
3. Use the document's content to answer the user's query.

## Frontend render actions — MUST use these to display results visually
- show_attachment_preview: ALWAYS call this after reading an attached document. Pass fileName, summary, and blobUrl.
- show_incident_alert: ALWAYS call this after get_active_incidents or get_system_status returns any incidents. Pass incidents as a JSON array. Never reply with plain text incident data.
- show_my_tickets: ALWAYS call this after search_tickets returns a list of tickets. Pass tickets as a JSON array. Never reply with plain text ticket lists.
- show_ticket_details: ALWAYS call after get_ticket to display the full ticket as a card. Pass id, title, description, priority, category, status, and optionally assignedTo and createdAt.
- show_kb_article: Call when presenting a specific KB article from context. Pass id, title, content, and optionally category.
- suggest_related_articles: Call when recommending 2–3 relevant KB articles. Pass articles as a JSON array with id, title, category, and summary.
- create_ticket: Call when the user wants to log or track an issue.

## Rules
1. When asked about incidents, outages, or system status — call get_active_incidents THEN call show_incident_alert with the results.
2. When asked to list tickets — call search_tickets THEN call show_my_tickets with the results.
3. When asked about a specific ticket — call get_ticket THEN call show_ticket_details with the result.
4. Always check get_system_status before troubleshooting — there may be an active incident causing the issue.
5. When presenting a KB article from context, call show_kb_article to render it as a card.
6. When recommending multiple KB articles, call suggest_related_articles with 2–3 options.
7. Even if you provide a text explanation, still call the render action so results appear as a card.
8. When a document is attached, read and acknowledge it before responding to the user's question.`;

export function HelpdeskChat() {
  const [page, setPage]       = useState<Page>("chat");
  const [tickets, setTickets]  = useState<Ticket[]>([]);
  const [refreshKey, setRefreshKey] = useState(0);
  const [attachedFiles, setAttachedFiles] = useState<AttachedFile[]>([]);
  const current = NAV_ITEMS.find(n => n.id === page)!;

  const { threadId } = useCopilotContext();
  const threadIdRef = useRef(threadId);
  threadIdRef.current = threadId;
  const sendTimeRef = useRef<number>(0);
  const waitingRef = useRef(false);
  const [lastStats, setLastStats] = useState<{ elapsedMs: number; promptTokens: number; completionTokens: number } | null>(null);

  const fetchStats = (retryDelay = 1000) => {
    const tid = threadIdRef.current;
    if (!tid || !waitingRef.current) return;
    fetch(`/api/copilotkit/usage?threadId=${encodeURIComponent(tid)}`)
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        if (data?.promptTokens) {
          setLastStats({ elapsedMs: Date.now() - sendTimeRef.current, ...data });
          waitingRef.current = false;
        } else if (waitingRef.current && retryDelay <= 3000) {
          setTimeout(() => fetchStats(retryDelay * 2), retryDelay);
        }
      })
      .catch(() => {
        if (waitingRef.current && retryDelay <= 3000) {
          setTimeout(() => fetchStats(retryDelay * 2), retryDelay);
        }
      });
  };

  // Inject stats chip into CopilotKit's controls row (right-aligned via margin-left:auto).
  // Uses a MutationObserver to re-inject if CopilotKit re-renders and removes the chip
  // (common after multi-turn/tool-call responses where CopilotKit settles state post-stream).
  useEffect(() => {
    document.getElementById("hd-stats-chip")?.remove();
    if (!lastStats) return;

    const chipText = `⏱ ${(lastStats.elapsedMs / 1000).toFixed(1)}s · 📥 ${lastStats.promptTokens} in / 📤 ${lastStats.completionTokens} out`;

    const inject = () => {
      const all = document.querySelectorAll(".copilotKitMessageControls");
      const controls = all[all.length - 1];
      if (!controls) return;
      const existing = document.getElementById("hd-stats-chip");
      if (existing?.parentElement === controls) return; // already in the right place
      existing?.remove();
      const el = document.createElement("span");
      el.id = "hd-stats-chip";
      el.style.cssText = "margin-left:auto;font-size:11px;color:#8892b0;font-family:monospace;letter-spacing:0.02em;white-space:nowrap;padding-top:2px";
      el.textContent = chipText;
      controls.appendChild(el);
    };

    inject();

    const observer = new MutationObserver(() => {
      const all = document.querySelectorAll(".copilotKitMessageControls");
      const last = all[all.length - 1];
      const chip = document.getElementById("hd-stats-chip");
      // Re-inject if chip is absent OR if it ended up in a stale (non-last) controls element
      if (!chip || (last && chip.parentElement !== last)) inject();
    });
    observer.observe(document.querySelector(".copilotKitMessages") ?? document.body, { childList: true, subtree: true });

    return () => { observer.disconnect(); document.getElementById("hd-stats-chip")?.remove(); };
  }, [lastStats]);

  const handleTicketCreated = (ticket: Ticket) => {
    setTickets(prev => [ticket, ...prev]);
    setRefreshKey(k => k + 1);
  };

  const handleAttachFile = async (file: File) => {
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
      const res = await fetch("/api/upload", { method: "POST", body, signal: controller.signal });
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
  };

  const handleRetryAttachment = (name: string) => {
    const entry = attachedFiles.find(f => f.name === name);
    if (!entry?.file) return;
    const fileToRetry = entry.file;
    setAttachedFiles(prev => prev.filter(f => f.name !== name));
    handleAttachFile(fileToRetry);
  };

  const handleRemoveAttachment = (name: string) => {
    setAttachedFiles(prev => prev.filter(f => f.name !== name));
  };

  const clearAllAttachments = () => setAttachedFiles([]);

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
          <AttachmentContext.Provider value={{
            attachedFiles,
            onAdd: handleAttachFile,
            onRemove: handleRemoveAttachment,
            onRetry: handleRetryAttachment,
            clearAll: clearAllAttachments,
            onSendStarted: () => { sendTimeRef.current = Date.now(); setLastStats(null); waitingRef.current = true; },
            onResponseComplete: () => fetchStats(500),
            onResponseReset: () => { setLastStats(null); waitingRef.current = true; },
          }}>
              <HelpdeskActions
                tickets={tickets}
                onTicketCreated={handleTicketCreated}
                attachedFiles={attachedFiles}
              />

              <div className="hd-chat-wrapper" style={{ ...ckTheme }}>
                <CopilotChat
                  Input={CustomChatInput}
                  instructions={AGENT_INSTRUCTIONS}
                  labels={{
                    title: "IT Support",
                    initial: "👋 Hi Alex! What IT issue can I help you with today?",
                    placeholder: "Describe your IT issue…",
                  }}
                />
              </div>
          </AttachmentContext.Provider>
        )}

        {page === "tickets"  && <TicketsPage agentTickets={tickets} refreshKey={refreshKey} />}
        {page === "kb"       && <KbPage />}
        {page === "settings" && <SettingsPage />}
      </main>
    </div>
  );
}