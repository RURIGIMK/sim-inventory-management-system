"use client";

import { useCallback, useEffect, useState } from "react";

const API = (process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5080").replace(/\/$/, "");
type AuditEvent = { id: number; actorEmail: string; action: string; entityType: string; entityId: string; details: string; createdAt: string; previousHash: string; hash: string };
type Verification = { valid: boolean; checkedEvents: number; firstInvalidEventId: number | null };

export default function AuditPage() {
  const [events, setEvents] = useState<AuditEvent[]>([]);
  const [verification, setVerification] = useState<Verification | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const request = useCallback(async (path: string) => {
    const token = sessionStorage.getItem("simAccessToken");
    if (!token) { window.location.href = "/login"; throw new Error("Sign in required."); }
    return fetch(API + path, { headers: { Authorization: "Bearer " + token }, cache: "no-store" });
  }, []);
  const load = useCallback(async () => {
    setLoading(true); setError("");
    try {
      const response = await request("/api/admin/audit");
      if (!response.ok) throw new Error(response.status === 403 ? "Administrator access is required to view the audit trail." : "Could not load audit events.");
      setEvents(await response.json() as AuditEvent[]);
      const check = await request("/api/admin/audit/verify");
      if (check.ok) setVerification(await check.json() as Verification);
    } catch (e) { setError(e instanceof Error ? e.message : "Could not load audit trail."); }
    finally { setLoading(false); }
  }, [request]);
  useEffect(() => { void load(); }, [load]);

  return <main className="admin-shell">
    <header className="admin-header"><div><a href="/" className="back-link">← Inventory</a><p className="auth-eyebrow">TRACEABILITY</p><h1>Audit trail</h1><p>Review recorded state changes and verify the hash-linked chain.</p></div><button className="text-button" onClick={() => { sessionStorage.removeItem("simAccessToken"); window.location.href = "/login"; }}>Sign out</button></header>
    {error && <p className="auth-error" role="alert">{error}</p>}
    <section className="admin-panel"><div className="audit-summary"><div><h2>Chain integrity</h2><p>Hash linking helps detect tampering. It is not absolute immutability.</p></div><button className="text-button" onClick={() => void load()}>Refresh and verify</button></div>
      {verification && <p className={verification.valid ? "admin-notice" : "auth-error"} role="status">{verification.valid ? `Verified ${verification.checkedEvents} audit events. No broken hash links detected.` : `Verification failed at event ${verification.firstInvalidEventId}. Stop using the audit result and investigate.`}</p>}
      {loading ? <p className="admin-footnote">Loading audit records…</p> : <div className="admin-table-wrap"><table className="admin-table audit-table"><thead><tr><th>Time (UTC)</th><th>Actor</th><th>Action</th><th>Entity</th><th>Details</th><th>Hash</th></tr></thead><tbody>
        {events.map(event => <tr key={event.id}><td>{new Date(event.createdAt).toLocaleString()}</td><td>{event.actorEmail}</td><td><strong>{event.action}</strong></td><td>{event.entityType} · {event.entityId}</td><td><code>{event.details}</code></td><td><code title={event.hash}>{event.hash.slice(0, 14)}…</code></td></tr>)}
      </tbody></table></div>}
    </section>
    <p className="admin-footnote">This interface displays the latest 500 events. A production deployment should add restricted database permissions, independent append-only archival, alerts, retention controls and tested backups.</p>
  </main>;
}
