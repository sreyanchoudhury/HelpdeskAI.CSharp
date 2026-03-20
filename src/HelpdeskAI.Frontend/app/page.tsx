"use client";

import { CopilotKit } from "@copilotkit/react-core";
import { HelpdeskChat } from "@/components/HelpdeskChat";
import { useSession } from "next-auth/react";

function LoadingScreen({ message }: { message: string }) {
  return (
    <div style={{
      minHeight: "100vh",
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      background: "#0a0b0f",
      color: "#9098b0",
      fontSize: 14,
      fontFamily: "system-ui, sans-serif",
    }}>
      {message}
    </div>
  );
}

export default function Home() {
  const { data: session, status } = useSession();

  if (status === "loading") {
    return <LoadingScreen message="Loading your HelpdeskAI session..." />;
  }

  if (!session?.user?.email) {
    return <LoadingScreen message="Redirecting to Microsoft Entra ID..." />;
  }

  return (
    <CopilotKit
      runtimeUrl="/api/copilotkit"
      agent="HelpdeskAgent"
      showDevConsole
      enableInspector
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
      <HelpdeskChat
        currentUser={{
          name: session.user.name?.trim() || session.user.email,
          email: session.user.email,
        }}
      />
    </CopilotKit>
  );
}
