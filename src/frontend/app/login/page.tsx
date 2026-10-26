"use client";

import Script from "next/script";
import { useCallback, useEffect, useState } from "react";

declare global {
  interface Window {
    google?: {
      accounts: {
        id: {
          initialize: (options: { client_id: string; callback: (response: { credential: string }) => void }) => void;
          renderButton: (element: HTMLElement, options: Record<string, unknown>) => void;
        };
      };
    };
  }
}

const API = (process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5080").replace(/\/$/, "");
const GOOGLE_CLIENT_ID = process.env.NEXT_PUBLIC_GOOGLE_CLIENT_ID || "";

type AuthReply = {
  status: string;
  token?: string;
  email?: string;
  displayName?: string;
  role?: string;
  message?: string;
};

export default function LoginPage() {
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const handleGoogleCredential = useCallback(async (credential: string) => {
    setBusy(true);
    setError("");
    try {
      const response = await fetch(API + "/api/auth/google", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ credential })
      });
      const body = await response.json() as AuthReply;
      if (!response.ok) throw new Error(body.message || "Google sign-in was rejected.");
      if (body.status !== "authenticated" || !body.token) {
        throw new Error("The server did not return an application session.");
      }
      sessionStorage.setItem("simAccessToken", body.token);
      window.location.href = "/";
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not sign in.");
    } finally {
      setBusy(false);
    }
  }, []);

  const initializeGoogle = useCallback(() => {
    const target = document.getElementById("google-signin-button");
    if (!target || !window.google || !GOOGLE_CLIENT_ID) return;
    target.replaceChildren();
    window.google.accounts.id.initialize({
      client_id: GOOGLE_CLIENT_ID,
      callback: response => { void handleGoogleCredential(response.credential); }
    });
    window.google.accounts.id.renderButton(target, { theme: "outline", size: "large", shape: "rectangular", text: "signin_with", width: 280 });
  }, [handleGoogleCredential]);

  useEffect(() => { initializeGoogle(); }, [initializeGoogle]);

  return (
    <>
      <Script src="https://accounts.google.com/gsi/client" strategy="afterInteractive" onLoad={initializeGoogle} />
      <main className="auth-shell">
        <section className="auth-card">
          <div className="auth-brand"><span className="brand-mark">S</span><div><strong>SIM Inventory</strong><small>ASSET CONTROL</small></div></div>
          <p className="auth-eyebrow">SECURE WORKSPACE</p>
          <h1>Sign in to continue</h1>
          <p className="auth-copy">Use the Google account that an administrator has approved for you.</p>
          <div id="google-signin-button" className="google-button-slot" />
          {!GOOGLE_CLIENT_ID && <p className="auth-hint">Google sign-in is not configured yet. Set NEXT_PUBLIC_GOOGLE_CLIENT_ID in the frontend environment.</p>}
          {busy && <p className="auth-hint">Signing you in…</p>}
          {error && <p className="auth-error" role="alert">{error}</p>}
          <p className="auth-footer">Access is limited to accounts approved by the system administrator. If you cannot sign in, contact the administrator.</p>
        </section>
      </main>
    </>
  );
}
