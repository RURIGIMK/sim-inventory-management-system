"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";

const API = (process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5080").replace(/\/$/, "");
type Sim = { id: number; phoneNumber: string; iccid: string; status: string };
type Employee = { id: number; employeeNumber: string; fullName: string; status: string };
type Assignment = { id: number; simId: number; simPhoneNumber: string; simIccid: string; employeeId: number; employeeNumber: string; employeeName: string; assignedAt: string; returnedAt?: string | null; isActive: boolean; notes: string };

export default function AllocationsPage() {
  const [sims, setSims] = useState<Sim[]>([]); const [employees, setEmployees] = useState<Employee[]>([]);
  const [assignments, setAssignments] = useState<Assignment[]>([]);
  const [simId, setSimId] = useState(""); const [employeeId, setEmployeeId] = useState(""); const [notes, setNotes] = useState("");
  const [reassignId, setReassignId] = useState(""); const [newEmployeeId, setNewEmployeeId] = useState("");
  const [notice, setNotice] = useState(""); const [error, setError] = useState("");
  const request = useCallback(async (path: string, init: RequestInit = {}) => {
    const token = sessionStorage.getItem("simAccessToken");
    if (!token) { window.location.href = "/login"; throw new Error("Sign in required."); }
    return fetch(API + path, { ...init, headers: { "Content-Type": "application/json", Authorization: "Bearer " + token, ...(init.headers || {}) } });
  }, []);
  const load = useCallback(async () => {
    const [s, e, a] = await Promise.all([request("/api/sims"), request("/api/employees"), request("/api/allocations")]);
    if ([s,e,a].some(r => !r.ok)) throw new Error("Could not load inventory, employee directory or allocation history.");
    setSims(await s.json() as Sim[]); setEmployees(await e.json() as Employee[]); setAssignments(await a.json() as Assignment[]);
  }, [request]);
  useEffect(() => { void load().catch(e => setError(e.message)); }, [load]);

  async function allocate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError(""); setNotice("");
    try {
      const response = await request("/api/allocations", { method: "POST", body: JSON.stringify({ simId: Number(simId), employeeId: Number(employeeId), notes }) });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(body.message || "Allocation failed. Manager or administrator access is required.");
      setNotice("SIM allocated and assignment history saved."); setSimId(""); setEmployeeId(""); setNotes(""); await load();
    } catch (e) { setError(e instanceof Error ? e.message : "Allocation failed."); }
  }
  async function returnSim(assignment: Assignment) {
    setError(""); setNotice("");
    try {
      const response = await request("/api/allocations/" + assignment.id + "/return", { method: "POST" });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(body.message || "Return failed. Manager or administrator access is required.");
      setNotice("SIM returned to inventory."); await load();
    } catch (e) { setError(e instanceof Error ? e.message : "Return failed."); }
  }
  async function reassign(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError(""); setNotice("");
    try {
      const response = await request("/api/allocations/" + reassignId + "/reassign", { method: "POST", body: JSON.stringify({ employeeId: Number(newEmployeeId), notes: "Reassigned through inventory interface" }) });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(body.message || "Reassignment failed. Manager or administrator access is required.");
      setNotice("SIM reassigned. The previous assignment remains in the history."); setReassignId(""); setNewEmployeeId(""); await load();
    } catch (e) { setError(e instanceof Error ? e.message : "Reassignment failed."); }
  }

  const active = assignments.filter(a => a.isActive);
  const available = sims.filter(s => s.status === "Available");
  const activeEmployees = employees.filter(e => e.status === "Active");
  return <main className="admin-shell">
    <header className="admin-header"><div><a href="/" className="back-link">← Inventory</a><p className="auth-eyebrow">ASSET CUSTODY</p><h1>SIM allocations</h1><p>Allocate, return and reassign SIMs while retaining each assignment record.</p></div><button className="text-button" onClick={() => { sessionStorage.removeItem("simAccessToken"); window.location.href = "/login"; }}>Sign out</button></header>
    {notice && <p className="admin-notice">{notice}</p>}{error && <p className="auth-error">{error}</p>}
    <section className="admin-panel"><h2>New allocation</h2><form className="admin-create-form allocation-form" onSubmit={allocate}>
      <label>Available SIM<select value={simId} onChange={e => setSimId(e.target.value)} required><option value="">Select a SIM</option>{available.map(s => <option key={s.id} value={s.id}>{s.phoneNumber} · {s.iccid}</option>)}</select></label>
      <label>Active employee<select value={employeeId} onChange={e => setEmployeeId(e.target.value)} required><option value="">Select employee</option>{activeEmployees.map(e => <option key={e.id} value={e.id}>{e.employeeNumber} · {e.fullName}</option>)}</select></label>
      <label>Notes (optional)<input value={notes} onChange={e => setNotes(e.target.value)} /></label>
      <button className="primary-button" type="submit">Allocate SIM</button>
    </form><p className="admin-footnote">Manager or administrator role required. Viewers can see history but cannot change allocations.</p></section>
    <section className="admin-panel"><h2>Reassign an active SIM</h2><form className="admin-create-form allocation-form" onSubmit={reassign}>
      <label>Current allocation<select value={reassignId} onChange={e => setReassignId(e.target.value)} required><option value="">Select allocation</option>{active.map(a => <option key={a.id} value={a.id}>{a.simPhoneNumber} · {a.employeeName}</option>)}</select></label>
      <label>New employee<select value={newEmployeeId} onChange={e => setNewEmployeeId(e.target.value)} required><option value="">Select employee</option>{activeEmployees.map(e => <option key={e.id} value={e.id}>{e.employeeNumber} · {e.fullName}</option>)}</select></label>
      <button className="primary-button" type="submit">Reassign SIM</button>
    </form></section>
    <section className="admin-panel"><h2>Assignment history</h2><div className="admin-table-wrap"><table className="admin-table"><thead><tr><th>SIM</th><th>Employee</th><th>Assigned</th><th>Returned</th><th>Status</th><th>Action</th></tr></thead><tbody>
      {assignments.map(a => <tr key={a.id}><td><strong>{a.simPhoneNumber}</strong><small>{a.simIccid}</small></td><td>{a.employeeName}<small>{a.employeeNumber}</small></td><td>{new Date(a.assignedAt).toLocaleString()}</td><td>{a.returnedAt ? new Date(a.returnedAt).toLocaleString() : "Not returned"}</td><td>{a.isActive ? "Active" : "Closed"}</td><td>{a.isActive ? <button className="text-button" onClick={() => void returnSim(a)}>Return</button> : "Archived"}</td></tr>)}
    </tbody></table></div></section>
  </main>;
}
