"use client";

import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";

type Sim = {
  id: number;
  operator: string;
  phoneNumber: string;
  iccid: string;
  planType: string;
  monthlyCost: number;
  contractStart?: string | null;
  contractEnd?: string | null;
  status: "Available" | "Allocated" | "Suspended" | "Deactivated";
  costCentre: string;
};

const API = (process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5080").replace(/\/$/, "");
const sample: Sim[] = [
  { id: 101, operator: "Safaricom", phoneNumber: "254700000101", iccid: "893100000000000101", planType: "Corporate voice + data", monthlyCost: 2400, status: "Allocated", costCentre: "OPS-014" },
  { id: 102, operator: "Airtel", phoneNumber: "254730000102", iccid: "893100000000000102", planType: "Data bundle", monthlyCost: 1500, status: "Available", costCentre: "IT-002" },
  { id: 103, operator: "Safaricom", phoneNumber: "254711000103", iccid: "893100000000000103", planType: "Corporate voice", monthlyCost: 950, status: "Suspended", costCentre: "FIN-008" },
  { id: 104, operator: "Telkom Kenya", phoneNumber: "254770000104", iccid: "893100000000000104", planType: "Field operations", monthlyCost: 1800, status: "Available", costCentre: "OPS-014" },
  { id: 105, operator: "Airtel", phoneNumber: "254733000105", iccid: "893100000000000105", planType: "Data bundle", monthlyCost: 1200, status: "Deactivated", costCentre: "HR-003" }
];

export default function Home() {
  const [sims, setSims] = useState<Sim[]>(sample);
  const [mode, setMode] = useState<"demo" | "api">("demo");
  const [connection, setConnection] = useState("Demo data loaded");
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("All statuses");
  const [notice, setNotice] = useState("");
  const [showForm, setShowForm] = useState(false);
  const [saving, setSaving] = useState(false);
  const [form, setForm] = useState({ operator: "Safaricom", phoneNumber: "", iccid: "", planType: "Corporate voice + data", monthlyCost: "1500", costCentre: "" });

  const loadSims = useCallback(async () => {
    try {
      const response = await fetch(API + "/api/sims", { cache: "no-store" });
      if (!response.ok) throw new Error("API returned " + response.status);
      const data = await response.json();
      if (!Array.isArray(data)) throw new Error("Unexpected API response");
      setSims(data);
      setMode("api");
      setConnection("Connected to API");
    } catch {
      setMode("demo");
      setConnection("Demo mode · API unavailable");
      setSims(current => current.length ? current : sample);
    }
  }, []);

  useEffect(() => { void loadSims(); }, [loadSims]);

  const filtered = useMemo(() => sims.filter(sim => {
    const haystack = [sim.operator, sim.phoneNumber, sim.iccid, sim.planType, sim.costCentre].join(" ").toLowerCase();
    return haystack.includes(query.toLowerCase()) && (status === "All statuses" || sim.status === status);
  }), [sims, query, status]);

  const totalMonthly = sims.reduce((sum, sim) => sum + Number(sim.monthlyCost || 0), 0);
  const activeCount = sims.filter(sim => sim.status === "Allocated").length;
  const availableCount = sims.filter(sim => sim.status === "Available").length;

  async function registerSim(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setNotice("");
    if (!form.phoneNumber.trim() || !form.iccid.trim() || !form.costCentre.trim()) {
      setNotice("Phone number, ICCID and cost centre are required.");
      return;
    }
    const newSim = {
      operator: form.operator,
      phoneNumber: form.phoneNumber.trim(),
      iccid: form.iccid.trim(),
      planType: form.planType,
      monthlyCost: Number(form.monthlyCost),
      costCentre: form.costCentre.trim(),
      status: "Available" as const
    };
    if (mode === "api") {
      setSaving(true);
      try {
        const response = await fetch(API + "/api/sims", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(newSim)
        });
        if (response.status === 409) throw new Error("A SIM with that ICCID or phone number already exists.");
        if (!response.ok) {
          const body = await response.json().catch(() => ({}));
          throw new Error(body.message || "Registration failed. Check the API and database.");
        }
        await loadSims();
        setNotice("SIM registered in the database.");
      } catch (error) {
        setNotice(error instanceof Error ? error.message : "Could not register SIM.");
        setSaving(false);
        return;
      }
      setSaving(false);
    } else {
      const duplicate = sims.some(sim => sim.iccid === newSim.iccid || sim.phoneNumber === newSim.phoneNumber);
      if (duplicate) {
        setNotice("Duplicate ICCID or phone number. Use unique demo values.");
        return;
      }
      setSims(current => [{ ...newSim, id: Math.max(0, ...current.map(item => item.id)) + 1, status: "Available" }, ...current]);
      setNotice("SIM added to this demo session. Connect the API to persist it in PostgreSQL.");
    }
    setForm({ operator: "Safaricom", phoneNumber: "", iccid: "", planType: "Corporate voice + data", monthlyCost: "1500", costCentre: "" });
    setShowForm(false);
  }

  return (
    <main className="app-shell">
      <aside className="sidebar">
        <div className="brand-lockup"><span className="brand-mark">S</span><div><strong>SIM Inventory</strong><small>ASSET CONTROL</small></div></div>
        <div className="workspace-label">WORKSPACE</div>
        <nav aria-label="Main navigation">
          <a className="nav-item selected" href="#inventory"><span className="nav-symbol">▦</span> Inventory <span className="nav-count">{sims.length}</span></a>
          <a className="nav-item" href="#overview"><span className="nav-symbol">◫</span> Overview</a>
          <a className="nav-item disabled" href="#allocation" onClick={e => { e.preventDefault(); setNotice("Allocation workflow is planned and not yet implemented."); }}><span className="nav-symbol">⇄</span> Allocations</a>
          <a className="nav-item disabled" href="#audit" onClick={e => { e.preventDefault(); setNotice("Audit log is planned and not yet implemented."); }}><span className="nav-symbol">≡</span> Audit log</a>
          <a className="nav-item disabled" href="#reports" onClick={e => { e.preventDefault(); setNotice("Reporting is planned and not yet implemented."); }}><span className="nav-symbol">▤</span> Reports</a>
        </nav>
        <div className="sidebar-bottom"><span className="status-led" /> Development environment <small>Version 0.1.0</small></div>
      </aside>

      <section className="main-panel">
        <header className="topbar">
          <div className="breadcrumbs">Workspace <span>/</span> <strong>Inventory</strong></div>
          <div className="topbar-right"><span className={mode === "api" ? "connection good" : "connection"}><i />{connection}</span><button className="avatar" aria-label="Current user">RK</button></div>
        </header>
        <div className="content">
          <section className="page-heading" id="overview">
            <div><div className="eyebrow">ASSET MANAGEMENT / SIM REGISTER</div><h1>SIM inventory</h1><p>Track corporate SIM assets, review status and maintain clean inventory records.</p></div>
            <button className="primary-button" onClick={() => { setShowForm(v => !v); setNotice(""); }}>{showForm ? "Close form" : <><span>＋</span> Register SIM</>}</button>
          </section>

          {notice && <div className="notice" role="status"><span>{notice}</span><button onClick={() => setNotice("")} aria-label="Dismiss notification">×</button></div>}

          {showForm && <section className="form-panel">
            <div className="panel-heading"><div><h2>Register a SIM</h2><p>Enter the inventory details. New SIMs start as available.</p></div></div>
            <form onSubmit={registerSim} className="sim-form">
              <label>Network operator<select value={form.operator} onChange={e => setForm({...form, operator:e.target.value})}><option>Safaricom</option><option>Airtel</option><option>Telkom Kenya</option><option>Other</option></select></label>
              <label>Phone number<input value={form.phoneNumber} onChange={e => setForm({...form, phoneNumber:e.target.value})} placeholder="2547XXXXXXXX" required /></label>
              <label>ICCID<input value={form.iccid} onChange={e => setForm({...form, iccid:e.target.value})} placeholder="SIM serial number" required /></label>
              <label>Plan type<select value={form.planType} onChange={e => setForm({...form, planType:e.target.value})}><option>Corporate voice + data</option><option>Corporate voice</option><option>Data bundle</option><option>Field operations</option></select></label>
              <label>Monthly cost (KES)<input type="number" min="0" step="0.01" value={form.monthlyCost} onChange={e => setForm({...form, monthlyCost:e.target.value})} required /></label>
              <label>Cost centre<input value={form.costCentre} onChange={e => setForm({...form, costCentre:e.target.value})} placeholder="e.g. OPS-014" required /></label>
              <div className="form-actions"><button className="secondary-button" type="button" onClick={() => setShowForm(false)}>Cancel</button><button className="primary-button" type="submit" disabled={saving}>{saving ? "Saving…" : "Save SIM"}</button></div>
            </form>
          </section>}

          <section className="metrics" aria-label="Inventory summary">
            <article className="metric-card"><div className="metric-label">TOTAL SIMS <span className="metric-icon">▦</span></div><div className="metric-value">{sims.length}</div><div className="metric-foot">Records in current view</div></article>
            <article className="metric-card"><div className="metric-label">ALLOCATED <span className="metric-icon red">↗</span></div><div className="metric-value">{activeCount}</div><div className="metric-foot">Currently assigned</div></article>
            <article className="metric-card"><div className="metric-label">AVAILABLE <span className="metric-icon">○</span></div><div className="metric-value">{availableCount}</div><div className="metric-foot">Ready for allocation</div></article>
            <article className="metric-card"><div className="metric-label">MONTHLY COST <span className="metric-icon">KES</span></div><div className="metric-value cost">{totalMonthly.toLocaleString("en-KE", { maximumFractionDigits: 0 })}</div><div className="metric-foot">Across listed SIMs · KES</div></article>
          </section>

          <section className="inventory-panel" id="inventory">
            <div className="panel-heading"><div><h2>SIM records</h2><p>Inventory details and current status</p></div><button className="text-button" onClick={() => void loadSims()}>↻ Refresh</button></div>
            <div className="table-toolbar"><div className="search-wrap"><span>⌕</span><input aria-label="Search inventory" placeholder="Search operator, number, ICCID or cost centre" value={query} onChange={e => setQuery(e.target.value)} /></div><select className="filter-select" aria-label="Filter by status" value={status} onChange={e => setStatus(e.target.value)}><option>All statuses</option><option>Available</option><option>Allocated</option><option>Suspended</option><option>Deactivated</option></select></div>
            <div className="table-scroll"><table><thead><tr><th>SIM / ICCID</th><th>OPERATOR</th><th>PLAN TYPE</th><th>COST CENTRE</th><th>MONTHLY COST</th><th>STATUS</th></tr></thead><tbody>
              {filtered.map(sim => <tr key={sim.id}><td><div className="sim-primary">{sim.phoneNumber}</div><div className="sim-secondary">ICCID {sim.iccid}</div></td><td><span className="operator-dot">{sim.operator.slice(0,1)}</span>{sim.operator}</td><td>{sim.planType}</td><td><span className="cost-centre">{sim.costCentre}</span></td><td className="money">{Number(sim.monthlyCost).toLocaleString("en-KE", {style:"currency",currency:"KES",maximumFractionDigits:0})}</td><td><span className={"status-pill " + sim.status.toLowerCase()}><i />{sim.status}</span></td></tr>)}
              {filtered.length === 0 && <tr><td colSpan={6} className="empty-state">No SIM records match these filters.</td></tr>}
            </tbody></table></div>
            <footer className="table-footer"><span>Showing <strong>{filtered.length}</strong> of <strong>{sims.length}</strong> records</span><span>{mode === "api" ? "Live API data" : "Synthetic demo data"}</span></footer>
          </section>
          <footer className="page-footer"><span>SIM Inventory Management System</span><span>Development build · 2026</span></footer>
        </div>
      </section>
    </main>
  );
}
