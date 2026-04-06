"use client";

import { useEffect, useMemo, useState } from "react";
import { CopilotKit } from "@copilotkit/react-core";
import { HelpdeskChat } from "@/components/HelpdeskChat";
import { signIn, useSession } from "next-auth/react";

const CHAT_RESET_EVENT = "helpdesk-reset-chat";

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
  const typedSession = session as (typeof session & { accessToken?: string; error?: string }) | null;
  const sessionError = typedSession?.error;
  const accessToken = typedSession?.accessToken;
  const needsReauth = status === "authenticated" && (!session?.user?.email || !accessToken || !!sessionError);
  // Session expired or user never signed in — redirect to Entra login.
  const needsSignIn = status === "unauthenticated";

  // All hooks MUST be called before any early returns (React Rules of Hooks).
  // Read agent mode from localStorage (set by the Settings toggle).
  // useMemo ensures this is read once on mount — mode switch triggers a full page reload.
  const agentName = useMemo(() => {
    if (typeof window === "undefined") return "HelpdeskAgent";
    return localStorage.getItem("agent-mode") === "v2" ? "helpdesk-v2" : "HelpdeskAgent";
  }, []);

  const showCopilotControls = useMemo(() => {
    if (typeof window === "undefined") return false;
    return localStorage.getItem("copilotkit-controls") === "visible";
  }, []);

  const [chatInstanceKey, setChatInstanceKey] = useState(0);

  useEffect(() => {
    if (needsReauth || needsSignIn) {
      void signIn("azure-ad", { callbackUrl: "/" });
    }
  }, [needsReauth, needsSignIn]);

  useEffect(() => {
    const handleChatReset = () => setChatInstanceKey(current => current + 1);
    window.addEventListener(CHAT_RESET_EVENT, handleChatReset);
    return () => window.removeEventListener(CHAT_RESET_EVENT, handleChatReset);
  }, []);

  if (status === "loading") {
    return <LoadingScreen message="Loading your HelpdeskAI session..." />;
  }

  if (needsReauth || needsSignIn) {
    return <LoadingScreen message="Redirecting to Microsoft Entra ID..." />;
  }

  if (!session?.user?.email) {
    // Fallback: session exists but email is missing — force re-auth.
    void signIn("azure-ad", { callbackUrl: "/" });
    return <LoadingScreen message="Redirecting to Microsoft Entra ID..." />;
  }

  return (
    <CopilotKit
      key={`${agentName}-${chatInstanceKey}`}
      runtimeUrl="/api/copilotkit"
      agent={agentName}
      showDevConsole={showCopilotControls}
      enableInspector={showCopilotControls}
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
