"use client";

import { useCallback, useEffect, useState } from "react";

const API = (process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5080").replace(/\/$/, "");
type Breakdown = { name: string; simCount: number; monthlyCost: number; allocated: number; available: number };
type Report = {
  generatedOn: string;
  inventory: { total: number; active: number; available: number; allocated: number; suspended: number; deactivated: number; monthlyCommittedCost: number };
  employees: { total: number; active: number; inactive: number };
  allocations: { active: number; historical: number; returned: number };
  contracts: { overdue: number; expiringWithin30Days: number };
  byOperator: Breakdown[];
  byCostCentre: Breakdown[];
  recentAssignments: { id: number; simPhoneNumber: string; operator: string; employeeNumber: string; employeeName: string; assignedAt: string; returnedAt: string | null; isActive: boolean }[];
};
const money = (value: number) => new Intl.NumberFormat("en-KE", { style: "currency", currency: "KES", maximumFractionDigits: 0 }).format(value);

function downloadCsv(report: Report) {
  const rows: string[][] = [
    ["SIM INVENTORY REPORT", new Date(report.generatedOn).toLocaleString()],
    [],
    ["INVENTORY SUMMARY"], ["Metric", "Value"],
    ["Total SIMs", String(report.inventory.total)], ["Active SIMs", String(report.inventory.active)],
    ["Available SIMs", String(report.inventory.available)], ["Allocated SIMs", String(report.inventory.allocated)],
    ["Suspended SIMs", String(report.inventory.suspended)], ["Deactivated SIMs", String(report.inventory.deactivated)],
    ["Monthly committed cost (KES)", String(report.inventory.monthlyCommittedCost)],
    [],
    ["COST BY OPERATOR"], ["Operator", "SIM count", "Monthly cost (KES)", "Allocated", "Available"],
    ...report.byOperator.map(x => [x.name, String(x.simCount), String(x.monthlyCost), String(x.allocated), String(x.available)]),
    [],
    ["COST BY COST CENTRE"], ["Cost centre", "SIM count", "Monthly cost (KES)", "Allocated", "Available"],
    ...report.byCostCentre.map(x => [x.name, String(x.simCount), String(x.monthlyCost), String(x.allocated), String(x.available)]),
    [],
    ["ALLOCATION HISTORY (latest 100)"], ["SIM number", "Operator", "Employee number", "Employee name", "Assigned at", "Returned at", "Status"],
    ...report.recentAssignments.map(x => [x.simPhoneNumber, x.operator, x.employeeNumber, x.employeeName, x.assignedAt, x.returnedAt ?? "", x.isActive ? "Active" : "Returned"])
  ];
  const csv = rows.map(row => row.map(value => '"' + value.replace(/"/g, '""') + '"').join(",")).join("\r\n");
  const url = URL.createObjectURL(new Blob(["\uFEFF" + csv], { type: "text/csv;charset=utf-8;" }));
  const link = document.createElement("a");
  link.href = url;
  link.download = "sim-inventory-report.csv";
  link.click();
  URL.revokeObjectURL(url);
}

export default function ReportsPage() {
  const [report, setReport] = useState<Report | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const load = useCallback(async () => {
    setLoading(true); setError("");
    const token = sessionStorage.getItem("simAccessToken");
    if (!token) { window.location.href = "/login"; return; }
    try {
      const response = await fetch(API + "/api/reports/summary", { cache: "no-store", headers: { Authorization: "Bearer " + token } });
      if (response.status === 401) { sessionStorage.removeItem("simAccessToken"); window.location.href = "/login"; return; }
      if (!response.ok) throw new Error(response.status === 403 ? "Your account cannot access reports." : "The reporting service could not be reached.");
      setReport(await response.json() as Report);
    } catch (e) { setError(e instanceof Error ? e.message : "Could not load report data."); }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);

  return <main className="admin-shell">
    <header className="admin-header">
      <div><a href="/" className="back-link">← Inventory</a><p className="auth-eyebrow">MANAGEMENT REPORTING</p><h1>SIM reports</h1><p>Current inventory position, allocation history and monthly cost distribution.</p></div>
      <div className="report-actions"><button className="text-button" onClick={() => void load()}>Refresh data</button><button className="primary-button" disabled={!report} onClick={() => report && downloadCsv(report)}>Export CSV</button></div>
    </header>
    <div className="intelligence-note"><strong>Reporting basis</strong><p>Figures are calculated from the current application database when this page loads. Monthly committed cost excludes deactivated SIMs. Allocation history includes the latest 100 records. Amounts are recorded monthly costs, not verified operator invoices.</p></div>
    {error && <p className="auth-error" role="alert">{error}</p>}
    {loading ? <section className="admin-panel"><p className="admin-footnote">Loading current report data…</p></section> : report && <>
      <div className="report-generated">Generated {new Date(report.generatedOn).toLocaleString("en-KE")}</div>
      <section className="intelligence-metrics report-metrics">
        <article className="intelligence-metric"><span>Total SIMs</span><strong>{report.inventory.total}</strong></article>
        <article className="intelligence-metric"><span>Allocated SIMs</span><strong>{report.inventory.allocated}</strong></article>
        <article className="intelligence-metric"><span>Available SIMs</span><strong>{report.inventory.available}</strong></article>
        <article className="intelligence-metric"><span>Monthly committed cost</span><strong className="report-money">{money(report.inventory.monthlyCommittedCost)}</strong></article>
        <article className="intelligence-metric"><span>Active employees</span><strong>{report.employees.active}</strong></article>
        <article className="intelligence-metric"><span>Active allocations</span><strong>{report.allocations.active}</strong></article>
      </section>
      <section className="admin-panel">
        <h2>Inventory status</h2><div className="report-status-grid">
          {(["available","allocated","suspended","deactivated"] as const).map(key => <div className="report-status" key={key}><span>{key}</span><strong>{report.inventory[key]}</strong></div>)}
        </div>
        <p className="admin-footnote">Contracts: {report.contracts.overdue} overdue · {report.contracts.expiringWithin30Days} expiring within 30 days. Employee directory: {report.employees.total} total, {report.employees.inactive} inactive/terminated.</p>
      </section>
      <section className="admin-panel">
        <h2>Monthly cost by operator</h2><div className="admin-table-wrap"><table className="admin-table report-table"><thead><tr><th>Operator</th><th>SIMs</th><th>Allocated</th><th>Available</th><th>Monthly cost</th></tr></thead><tbody>
          {report.byOperator.map(x => <tr key={x.name}><td><strong>{x.name}</strong></td><td>{x.simCount}</td><td>{x.allocated}</td><td>{x.available}</td><td>{money(x.monthlyCost)}</td></tr>)}
          {!report.byOperator.length && <tr><td colSpan={5}>No operator records available.</td></tr>}
        </tbody></table></div>
      </section>
      <section className="admin-panel">
        <h2>Monthly cost by cost centre</h2><div className="admin-table-wrap"><table className="admin-table report-table"><thead><tr><th>Cost centre</th><th>SIMs</th><th>Allocated</th><th>Available</th><th>Monthly cost</th></tr></thead><tbody>
          {report.byCostCentre.map(x => <tr key={x.name}><td><strong>{x.name}</strong></td><td>{x.simCount}</td><td>{x.allocated}</td><td>{x.available}</td><td>{money(x.monthlyCost)}</td></tr>)}
          {!report.byCostCentre.length && <tr><td colSpan={5}>No cost centre records available.</td></tr>}
        </tbody></table></div>
      </section>
      <section className="admin-panel">
        <h2>Allocation history</h2><p className="admin-footnote">Latest {report.recentAssignments.length} records, including returned assignments.</p>
        <div className="admin-table-wrap"><table className="admin-table report-table"><thead><tr><th>SIM number</th><th>Operator</th><th>Employee</th><th>Assigned</th><th>Returned</th><th>Status</th></tr></thead><tbody>
          {report.recentAssignments.map(x => <tr key={x.id}><td>{x.simPhoneNumber}</td><td>{x.operator}</td><td><strong>{x.employeeName}</strong><small>{x.employeeNumber}</small></td><td>{new Date(x.assignedAt).toLocaleDateString("en-KE")}</td><td>{x.returnedAt ? new Date(x.returnedAt).toLocaleDateString("en-KE") : "—"}</td><td><span className={x.isActive ? "account-active" : "account-disabled"}>{x.isActive ? "Active" : "Returned"}</span></td></tr>)}
          {!report.recentAssignments.length && <tr><td colSpan={6}>No allocation history available.</td></tr>}
        </tbody></table></div>
      </section>
    </>}
  </main>;
}
