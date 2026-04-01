"use client";

import { CopilotKit } from "@copilotkit/react-core";
import { HelpdeskChat } from "@/components/HelpdeskChat";

const BANNER_H = 34; // px — keep in sync with the style override below

export default function DemoPage() {
  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100dvh" }}>
      {/* Scoped overrides — globals.css has different rules per breakpoint:
          Desktop: .hd-shell{height:100dvh}  .hd-main{height:100vh}
          Mobile:  .hd-shell{height:auto; min-height:100dvh}  .hd-main{height:auto}
          Both need the banner height subtracted at every breakpoint. */}
      <style>{`
        .hd-shell { height: calc(100dvh - ${BANNER_H}px) !important; }
        .hd-main  { height: calc(100dvh - ${BANNER_H}px) !important; }
        @media (max-width: 960px) {
          .hd-shell { height: auto !important; min-height: calc(100dvh - ${BANNER_H}px) !important; }
          .hd-main  { height: auto !important; }
        }
      `}</style>

      {/* Banner — in normal flow so it participates in flex sizing */}
      <div
        style={{
          height: BANNER_H,
          flexShrink: 0,
          background: "#856404",
          color: "#fff8e1",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          gap: 6,
          fontSize: 13,
          fontFamily: "system-ui, sans-serif",
        }}
      >
        ⚠️ Demo Mode — Internal preview only. Not for production use.
      </div>

      {/* Chat fills the remaining height exactly */}
      <div style={{ flex: 1, overflow: "hidden" }}>
        <CopilotKit runtimeUrl="/api/copilotkit/demo" agent="HelpdeskAgent">
          <HelpdeskChat currentUser={{ name: "Demo User", email: "" }} />
        </CopilotKit>
      </div>
    </div>
  );
}
