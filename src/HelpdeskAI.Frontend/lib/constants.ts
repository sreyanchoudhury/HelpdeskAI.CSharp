// Shared display constants used across HelpdeskChat and HelpdeskActions.

export const DEMO_USER = "alex.johnson@contoso.com";

export const PRIORITY_COLOR: Record<string, string> = {
  low: "#22c55e", medium: "#f59e0b", high: "#ef4444", critical: "#9333ea",
};

export const PRIORITY_BG: Record<string, string> = {
  low: "#22c55e18", medium: "#f59e0b18", high: "#ef444418", critical: "#9333ea18",
};

export const CATEGORY_ICON: Record<string, string> = {
  hardware: "🖥️", software: "💻", network: "🌐",
  access: "🔑", email: "📧", vpn: "🔒", other: "🎫",
};

export const HEALTH_COLOR: Record<string, string> = {
  outage: "#ef4444", degraded: "#f59e0b", maintenance: "#3d5afe", operational: "#22c55e",
};

export const HEALTH_BG: Record<string, string> = {
  outage: "#ef444418", degraded: "#f59e0b18", maintenance: "#3d5afe18", operational: "#22c55e18",
};

export const HEALTH_ICON: Record<string, string> = {
  outage: "🔴", degraded: "⚠️", maintenance: "🔧", operational: "✅",
};

export const KB_CATEGORY_COLOR: Record<string, string> = {
  vpn: "#6366f1", email: "#3b82f6", hardware: "#f59e0b",
  network: "#8b5cf6", access: "#ec4899", printing: "#14b8a6",
  software: "#22c55e", other: "#9098b0",
};

export const KB_CAT_ICON: Record<string, string> = {
  vpn: "🔐", email: "📧", hardware: "🖥️", network: "🌐",
  access: "🔑", printing: "🖨️", software: "💿", other: "📚",
};
