"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";

const API = (process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5080").replace(/\/$/, "");
type Period = { period: string; records: number; billed: number; contracted: number; ruleFlags: number; synthetic: number };
type Summary = { totalRecords: number; periods: Period[] };

const money = (value: number) => new Intl.NumberFormat("en-KE", { style: "currency", currency: "KES", maximumFractionDigits: 0 }).format(value);

export default function BillingPage() {
  const [summary, setSummary] = useState<Summary | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [errors, setErrors] = useState<string[]>([]);
  const [replace, setReplace] = useState(false);
  const [busy, setBusy] = useState(false);
  const [role, setRole] = useState("");

  const request = useCallback((path: string, init: RequestInit = {}) => {
    const token = sessionStorage.getItem("simAccessToken");
    if (!token) { window.location.href = "/login"; throw new Error("Sign in required."); }
    return fetch(API + path, { ...init, cache: "no-store", headers: { Authorization: "Bearer " + token, ...(init.headers || {}) } });
  }, []);

  const load = useCallback(async () => {
    setError("");
    try {
      const response = await request("/api/billing/summary");
      if (response.status === 401) { sessionStorage.removeItem("simAccessToken"); window.location.href = "/login"; return; }
      if (!response.ok) throw new Error("Could not load billing history.");
      setSummary(await response.json() as Summary);
    } catch (e) { setError(e instanceof Error ? e.message : "Could not load billing history."); }
  }, [request]);

  useEffect(() => {
    void load();
    request("/api/auth/me").then(r => r.ok ? r.json() : null).then(me => setRole(me?.role ?? "")).catch(() => setRole(""));
  }, [load, request]);

  async function upload(file: File | undefined) {
    if (!file) return;
    setBusy(true); setNotice(""); setErrors([]);
    try {
      const response = await request(`/api/admin/billing/import?replace=${replace}`, {
        method: "POST", body: await file.text(), headers: { "Content-Type": "text/csv" }
      });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) { setErrors(body.errors ?? []); throw new Error(body.message || "The invoice was not imported."); }
      setNotice(`Invoice imported: ${body.created} new lines, ${body.replaced} corrected. The next model run will include them.`);
      await load();
    } catch (e) { setNotice(e instanceof Error ? e.message : "The invoice was not imported."); }
    finally { setBusy(false); }
  }

  return <main className="admin-shell">
    <header className="admin-header">
      <div>
        <Link href="/" className="back-link">← Inventory</Link>
        <p className="auth-eyebrow">COST CONTROL</p>
        <h1>Billing history</h1>
        <p>What operators actually billed each month, compared with each SIM&apos;s contracted fee. This history is what the anomaly model learns from.</p>
      </div>
      <button className="text-button" onClick={() => { sessionStorage.removeItem("simAccessToken"); window.location.href = "/login"; }}>Sign out</button>
    </header>

    {error && <p className="auth-error" role="alert">{error}</p>}

    {role === "Administrator" && <section className="admin-panel">
      <h2>Import an operator invoice</h2>
      <p>CSV columns: <code>iccid</code> or <code>phone_number</code>, <code>period</code> (YYYY-MM), <code>amount</code>, and optionally <code>data_mb</code> and <code>voice_minutes</code>. The whole file is rejected if any row is wrong, so an invoice is never half-imported.</p>
      <div className="allocation-form">
        <label><input type="checkbox" checked={replace} onChange={e => setReplace(e.target.checked)} /> Correct months that were already imported</label>
        <label>Invoice (.csv)<input type="file" accept=".csv,text/csv" disabled={busy} onChange={e => { void upload(e.target.files?.[0]); e.target.value = ""; }} /></label>
      </div>
      {notice && <p className="admin-notice" role="status">{notice}</p>}
      {errors.length > 0 && <ul className="auth-error">{errors.map(e => <li key={e}>{e}</li>)}</ul>}
    </section>}

    <section className="admin-panel">
      <div className="panel-heading">
        <div><h2>Monthly totals</h2><p>{summary ? `${summary.totalRecords} SIM-month records` : "Loading…"}</p></div>
        <Link className="text-button" href="/intelligence">Review cost anomalies →</Link>
      </div>
      <div className="admin-table-wrap"><table className="admin-table">
        <thead><tr><th>Month</th><th>SIMs billed</th><th>Billed</th><th>Contracted</th><th>Difference</th><th>Rule flags</th></tr></thead>
        <tbody>
          {summary?.periods.map(p => <tr key={p.period}>
            <td><strong>{p.period}</strong>{p.synthetic > 0 && <small>{p.synthetic === p.records ? "synthetic demo data" : `${p.synthetic} synthetic lines`}</small>}</td>
            <td>{p.records}</td><td>{money(p.billed)}</td><td>{money(p.contracted)}</td>
            <td>{money(p.billed - p.contracted)}</td><td>{p.ruleFlags}</td>
          </tr>)}
          {summary && summary.periods.length === 0 && <tr><td colSpan={6}>No billing history yet. Import an operator invoice to start.</td></tr>}
        </tbody>
      </table></div>
      <p className="admin-footnote">Rule flags mark a month billed at 1.5× or more, or 0.5× or less, of the contracted fee. They are review prompts, not findings.</p>
    </section>
  </main>;
}
