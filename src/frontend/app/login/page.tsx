"use client";

import Script from "next/script";
import { FormEvent, useCallback, useEffect, useState } from "react";

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
  totpSetupUri?: string;
  manualSecret?: string;
  message?: string;
};

export default function LoginPage() {
  const [stage, setStage] = useState<"google" | "setup" | "otp">("google");
  const [pendingToken, setPendingToken] = useState("");
  const [email, setEmail] = useState("");
  const [manualSecret, setManualSecret] = useState("");
  const [setupUri, setSetupUri] = useState("");
  const [code, setCode] = useState("");
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
      if (!body.token) throw new Error("The server did not return a sign-in challenge.");
      setPendingToken(body.token);
      setEmail(body.email || "");
      sessionStorage.setItem("simPendingToken", body.token);
      if (body.status === "mfa_setup_required") {
        setStage("setup");
        setManualSecret(body.manualSecret || "");
        setSetupUri(body.totpSetupUri || "");
      } else if (body.status === "mfa_required") {
        setStage("otp");
      } else if (body.status === "authenticated") {
        sessionStorage.setItem("simAccessToken", body.token);
        window.location.href = "/";
      } else {
        throw new Error("Unexpected authentication response.");
      }
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

  async function verifyCode(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      const endpoint = stage === "setup" ? "/api/auth/mfa/setup" : "/api/auth/otp";
      const response = await fetch(API + endpoint, {
        method: "POST",
        headers: { "Content-Type": "application/json", Authorization: "Bearer " + pendingToken },
        body: JSON.stringify({ code: code.trim() })
      });
      const body = await response.json() as AuthReply;
      if (!response.ok) throw new Error(body.message || "The one-time code was not accepted.");
      if (!body.token) throw new Error("The server did not return an application session.");
      sessionStorage.setItem("simAccessToken", body.token);
      sessionStorage.removeItem("simPendingToken");
      window.location.href = "/";
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not verify the code.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <Script src="https://accounts.google.com/gsi/client" strategy="afterInteractive" onLoad={initializeGoogle} />
      <main className="auth-shell">
        <section className="auth-card">
          <div className="auth-brand"><span className="brand-mark">S</span><div><strong>SIM Inventory</strong><small>ASSET CONTROL</small></div></div>
          <p className="auth-eyebrow">SECURE WORKSPACE</p>
          <h1>{stage === "google" ? "Sign in to continue" : stage === "setup" ? "Set up your authenticator" : "Verify it’s you"}</h1>
          <p className="auth-copy">
            {stage === "google"
              ? "Use the Google account that an administrator has provisioned for you."
              : stage === "setup"
                ? "Add this account to Google Authenticator, Microsoft Authenticator, or another TOTP app, then enter the current six-digit code."
                : "Enter the current six-digit code from your authenticator app."}
          </p>
          {email && <p className="auth-account">{email}</p>}
          {stage === "google" && <>
            <div id="google-signin-button" className="google-button-slot" />
            {!GOOGLE_CLIENT_ID && <p className="auth-hint">Google sign-in is not configured yet. Set NEXT_PUBLIC_GOOGLE_CLIENT_ID in the frontend environment.</p>}
          </>}
          {stage === "setup" && <>
            <div className="secret-panel"><span>Manual setup key</span><code>{manualSecret || "Loading…"}</code><button type="button" onClick={() => { void navigator.clipboard.writeText(manualSecret); }}>Copy key</button></div>
            <details><summary>Authenticator setup URI</summary><code className="uri-text">{setupUri}</code></details>
            <form onSubmit={verifyCode} className="otp-form">
              <label htmlFor="otp-code">Six-digit verification code</label>
              <input id="otp-code" inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" maxLength={6} value={code} onChange={e => setCode(e.target.value)} required />
              <button className="primary-button" type="submit" disabled={busy}>{busy ? "Verifying…" : "Verify and enable MFA"}</button>
            </form>
          </>}
          {stage === "otp" && <form onSubmit={verifyCode} className="otp-form">
            <label htmlFor="otp-code">Six-digit verification code</label>
            <input id="otp-code" inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" maxLength={6} value={code} onChange={e => setCode(e.target.value)} required />
            <button className="primary-button" type="submit" disabled={busy}>{busy ? "Verifying…" : "Verify and sign in"}</button>
          </form>}
          {busy && <p className="auth-hint">Contacting the authentication service…</p>}
          {error && <p className="auth-error" role="alert">{error}</p>}
          <p className="auth-footer">Accounts are invitation-only. If your Google account is not provisioned, contact the system administrator.</p>
        </section>
      </main>
    </>
  );
}
