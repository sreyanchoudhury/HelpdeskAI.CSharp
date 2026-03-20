"use client";

import { useEffect } from "react";
import { signIn } from "next-auth/react";

export default function SignInPage() {
  useEffect(() => {
    void signIn("azure-ad", { callbackUrl: "/" });
  }, []);

  return (
    <main style={{
      minHeight: "100vh",
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      background: "radial-gradient(circle at top, #1b2140 0%, #0a0b0f 55%)",
      color: "#e8eaf0",
      padding: 24,
      fontFamily: "system-ui, sans-serif",
    }}>
      <div style={{
        width: "100%",
        maxWidth: 460,
        background: "rgba(15,17,23,0.92)",
        border: "1px solid rgba(255,255,255,0.1)",
        borderRadius: 18,
        padding: 30,
        boxShadow: "0 18px 60px rgba(0,0,0,0.35)",
      }}>
        <div style={{ fontSize: 12, letterSpacing: "0.08em", textTransform: "uppercase", color: "#5a6280", marginBottom: 12 }}>
          HelpdeskAI
        </div>
        <h1 style={{ fontSize: 28, margin: 0, marginBottom: 10 }}>Redirecting to Microsoft Entra</h1>
        <p style={{ fontSize: 14, lineHeight: 1.6, color: "#9098b0", margin: 0, marginBottom: 20 }}>
          You&apos;ll be taken to your organization&apos;s sign-in page in a moment.
        </p>
        <button
          onClick={() => signIn("azure-ad", { callbackUrl: "/" })}
          style={{
            width: "100%",
            border: "none",
            borderRadius: 10,
            background: "#3d5afe",
            color: "#fff",
            fontSize: 14,
            fontWeight: 600,
            padding: "12px 16px",
            cursor: "pointer",
          }}
        >
          Sign in with Entra
        </button>
      </div>
    </main>
  );
}
