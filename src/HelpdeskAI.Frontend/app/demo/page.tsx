"use client";

import { CopilotKit } from "@copilotkit/react-core";
import { HelpdeskChat } from "@/components/HelpdeskChat";

const BANNER_H = 34; // px

export default function DemoPage() {
  return (
    <div className="hd-demo-root">
      <style>{`
        /* ── Desktop: fixed viewport layout (mirrors the normal app shell) ───── */
        .hd-demo-root    { display: flex; flex-direction: column;
                           height: 100dvh; overflow: hidden; }
        .hd-demo-content { flex: 1; overflow: hidden; }
        .hd-shell        { height: calc(100dvh - ${BANNER_H}px) !important; }
        .hd-main         { height: calc(100dvh - ${BANNER_H}px) !important; }

        /* ── Mobile (≤960px): natural height, body scrolls ────────────────── */
        @media (max-width: 960px) {
          .hd-demo-root    { height: auto; min-height: 100dvh; overflow: visible; }
          .hd-demo-content { overflow: visible; }
          .hd-shell        { height: auto !important;
                             min-height: calc(100dvh - ${BANNER_H}px) !important; }
          .hd-main         { height: auto !important; }
        }
      `}</style>

      {/* Banner — always in normal flow, participates in flex sizing */}
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

      <div className="hd-demo-content">
        <CopilotKit runtimeUrl="/api/copilotkit/demo" agent="HelpdeskAgent">
          <HelpdeskChat currentUser={{ name: "Demo User", email: "" }} />
        </CopilotKit>
      </div>
    </div>
  );
}
