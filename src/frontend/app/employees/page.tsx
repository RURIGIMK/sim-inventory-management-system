"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";

const API = (process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5080").replace(/\/$/, "");
type Employee = { id: number; employeeNumber: string; fullName: string; email?: string | null; department: string; costCentre: string; status: "Active" | "Inactive" | "Terminated"; source?: string; lastSyncedAt?: string | null };
type SyncRun = { id: number; source: string; mode: string; startedAt: string; received: number; created: number; updated: number; deactivated: number; unchanged: number; rejected: number; outcome: string; triggeredBy: string; errors?: string[] };
type SyncStatus = { pullConfigured: boolean; webhookConfigured: boolean; runs: SyncRun[] };
type Holder = { assignmentId: number; simId: number; phoneNumber: string; employeeNumber: string; fullName: string };
const sourceLabel: Record<string, string> = { seed: "Demo seed", csv_import: "HR CSV export", hr_api: "HR API (pull)", hr_webhook: "HR push" };

export default function EmployeesPage() {
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [query, setQuery] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [sync, setSync] = useState<SyncStatus | null>(null);
  const [mode, setMode] = useState("snapshot");
  const [allowLarge, setAllowLarge] = useState(false);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState("");
  const [holders, setHolders] = useState<Holder[]>([]);
  const [rejections, setRejections] = useState<string[]>([]);

  const request = useCallback(async (path: string, init: RequestInit = {}) => {
    const value = sessionStorage.getItem("simAccessToken") || "";
    if (!value) {
      window.location.href = "/login";
      throw new Error("Sign in required.");
    }
    return fetch(API + path, {
      ...init,
      headers: { "Content-Type": "application/json", Authorization: "Bearer " + value, ...(init.headers || {}) }
    });
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const response = await request("/api/employees", { cache: "no-store" });
      if (!response.ok) throw new Error("Could not load the employee directory. Check the API connection and your session.");
      setEmployees(await response.json() as Employee[]);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not load the employee directory.");
    } finally {
      setLoading(false);
    }
  }, [request]);

  const loadSync = useCallback(async () => {
    const response = await request("/api/admin/employees/sync-runs", { cache: "no-store" });
    if (response.ok) setSync(await response.json() as SyncStatus); // 403 for non-administrators: panel stays hidden
  }, [request]);

  useEffect(() => { void load(); void loadSync(); }, [load, loadSync]);

  async function runSync(path: string, init: RequestInit) {
    setBusy(true); setNotice(""); setHolders([]); setRejections([]);
    try {
      const response = await request(path, init);
      const body = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(body.message || "The directory sync failed.");
      const run = body.run;
      setNotice(`Directory updated from ${sourceLabel[run.source] ?? run.source}: ${run.created} added, ${run.updated} updated, ${run.deactivated} deactivated, ${run.unchanged} unchanged, ${run.rejected} rejected.`);
      setHolders(body.holdersNeedingRecovery ?? []);
      setRejections(body.errors ?? []);
      await load(); await loadSync();
    } catch (e) {
      setNotice(e instanceof Error ? e.message : "The directory sync failed.");
    } finally {
      setBusy(false);
    }
  }

  async function importCsv(file: File | undefined) {
    if (!file) return;
    const text = await file.text();
    await runSync(`/api/admin/employees/import?mode=${mode}&allowLargeDeactivation=${allowLarge}`,
      { method: "POST", body: text, headers: { "Content-Type": "text/csv" } });
  }

  const filteredEmployees = useMemo(() => {
    const search = query.trim().toLowerCase();
    if (!search) return employees;
    return employees.filter(employee =>
      [employee.employeeNumber, employee.fullName, employee.email || "", employee.department, employee.costCentre, employee.status]
        .some(value => value.toLowerCase().includes(search))
    );
  }, [employees, query]);

  return <main className="admin-shell">
    <header className="admin-header">
      <div>
        <Link href="/" className="back-link">← Inventory</Link>
        <p className="auth-eyebrow">PEOPLE DIRECTORY</p>
        <h1>Employees</h1>
        <p>Read-only directory used to select employees for SIM allocation.</p>
      </div>
      <button className="text-button" onClick={() => { sessionStorage.removeItem("simAccessToken"); window.location.href = "/login"; }}>Sign out</button>
    </header>

    {sync && <section className="admin-panel">
      <div className="panel-heading">
        <div>
          <h2>HR directory integration</h2>
          <p>HR API pull: <strong>{sync.pullConfigured ? "connected" : "not configured"}</strong> · HR push (signed webhook): <strong>{sync.webhookConfigured ? "enabled" : "not configured"}</strong></p>
        </div>
        <button className="primary-button" disabled={busy || !sync.pullConfigured} onClick={() => void runSync("/api/admin/employees/sync", { method: "POST" })}>{busy ? "Syncing…" : "Sync from HR now"}</button>
      </div>
      <p className="admin-footnote">Until the HR API is connected, upload the HR export as CSV with columns employee_number, full_name, email, department, cost_centre, status. A full export (snapshot) also deactivates anyone missing from it; a partial file (delta) only adds and updates.</p>
      <div className="allocation-form">
        <label>Export type<select value={mode} onChange={e => setMode(e.target.value)}><option value="snapshot">Full export (snapshot)</option><option value="delta">Changes only (delta)</option></select></label>
        <label><input type="checkbox" checked={allowLarge} onChange={e => setAllowLarge(e.target.checked)} /> Allow a snapshot that deactivates more than 30% of staff</label>
        <label>HR export (.csv)<input type="file" accept=".csv,text/csv" disabled={busy} onChange={e => { void importCsv(e.target.files?.[0]); e.target.value = ""; }} /></label>
      </div>
      {notice && <p className="admin-notice" role="status">{notice}</p>}
      {holders.length > 0 && <div className="auth-error" role="alert">
        <strong>{holders.length} SIM{holders.length === 1 ? " is" : "s are"} held by people who are no longer active.</strong> Recover or reassign them on the <Link href="/allocations">Allocations</Link> page:
        <ul>{holders.map(h => <li key={h.assignmentId}>{h.phoneNumber} — {h.fullName} ({h.employeeNumber})</li>)}</ul>
      </div>}
      {rejections.length > 0 && <details><summary>{rejections.length} rows rejected</summary><ul>{rejections.map(r => <li key={r}>{r}</li>)}</ul></details>}
      {sync.runs.length > 0 && <div className="admin-table-wrap"><table className="admin-table">
        <thead><tr><th>When</th><th>Source</th><th>Received</th><th>Added</th><th>Updated</th><th>Deactivated</th><th>Rejected</th></tr></thead>
        <tbody>{sync.runs.slice(0, 8).map(run => <tr key={run.id}>
          <td>{new Date(run.startedAt).toLocaleString("en-KE")}<small>{run.triggeredBy}</small></td>
          <td>{sourceLabel[run.source] ?? run.source} · {run.mode}</td>
          <td>{run.received}</td><td>{run.created}</td><td>{run.updated}</td><td>{run.deactivated}</td><td>{run.rejected}</td>
        </tr>)}</tbody>
      </table></div>}
    </section>}

    <section className="admin-panel">
      <div className="panel-heading">
        <div>
          <h2>Employee directory</h2>
          <p>{employees.length} records available for allocation</p>
        </div>
        <button className="text-button" onClick={() => void load()} disabled={loading}>{loading ? "Refreshing..." : "Refresh directory"}</button>
      </div>
      <p className="admin-footnote">The directory is read-only here: people are added, updated and deactivated only by the HR system of record. Names shown in this demonstration are fictional.</p>
      <label className="employee-search-label">Search directory
        <input value={query} onChange={e => setQuery(e.target.value)} placeholder="Search name, employee number, department or cost centre" />
      </label>
      {error && <p className="auth-error" role="alert">{error}</p>}
      <div className="admin-table-wrap">
        <table className="admin-table">
          <thead><tr><th>Employee</th><th>Department</th><th>Cost centre</th><th>Status</th></tr></thead>
          <tbody>
            {filteredEmployees.map(employee => <tr key={employee.id}>
              <td><strong>{employee.fullName}</strong><small>{employee.employeeNumber}{employee.email ? " · " + employee.email : ""}</small></td>
              <td>{employee.department}</td>
              <td>{employee.costCentre}</td>
              <td>{employee.status}<small>{sourceLabel[employee.source ?? ""] ?? employee.source}{employee.lastSyncedAt ? " · synced " + new Date(employee.lastSyncedAt).toLocaleDateString("en-KE") : ""}</small></td>
            </tr>)}
            {!loading && filteredEmployees.length === 0 && <tr><td colSpan={4}>{error ? "Directory data could not be loaded." : "No employees match this search."}</td></tr>}
          </tbody>
        </table>
      </div>
    </section>
  </main>;
}
