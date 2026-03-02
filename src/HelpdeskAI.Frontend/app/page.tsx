"use client";

import { CopilotKit } from "@copilotkit/react-core";
import { HelpdeskChat } from "@/components/HelpdeskChat";

export default function Home() {
  return (
    <CopilotKit
      runtimeUrl="/api/copilotkit"
      agent="HelpdeskAgent"
      onError={(event) => {
        // CopilotErrorEvent shape: { error: unknown } — not Error directly.
        // Suppress browser extension noise (React DevTools, Chrome extensions)
        // that send unknown actions into CopilotKit's event bus.
        const msg = String(
          event && typeof event === "object" && "error" in event
            ? (event as { error: unknown }).error
            : event
        );
        if (
          msg.includes("unknown action:") ||
          msg.includes("SharedStorage") ||
          msg.includes("WebSocketConnection")
        ) {
          return;
        }
        console.error("[CopilotKit error]", event);
      }}
    >
      <HelpdeskChat />
    </CopilotKit>
  );
}