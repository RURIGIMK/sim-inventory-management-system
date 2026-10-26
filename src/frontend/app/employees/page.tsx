"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

const API = (process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5080").replace(/\/$/, "");
type Employee = { id: number; employeeNumber: string; fullName: string; email?: string | null; department: string; costCentre: string; status: "Active" | "Inactive" | "Terminated" };

export default function EmployeesPage() {
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [query, setQuery] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

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

  useEffect(() => { void load(); }, [load]);

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
        <a href="/" className="back-link">← Inventory</a>
        <p className="auth-eyebrow">PEOPLE DIRECTORY</p>
        <h1>Employees</h1>
        <p>Read-only directory used to select employees for SIM allocation.</p>
      </div>
      <button className="text-button" onClick={() => { sessionStorage.removeItem("simAccessToken"); window.location.href = "/login"; }}>Sign out</button>
    </header>

    <section className="admin-panel">
      <div className="panel-heading">
        <div>
          <h2>Employee directory</h2>
          <p>{employees.length} records available for allocation</p>
        </div>
        <button className="text-button" onClick={() => void load()} disabled={loading}>{loading ? "Refreshing..." : "Refresh directory"}</button>
      </div>
      <p className="admin-notice">Demo data notice: these are synthetic seed records, not a connection to a real company HR database. A real deployment must use an approved HR data feed.</p>
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
              <td>{employee.status}</td>
            </tr>)}
            {!loading && filteredEmployees.length === 0 && <tr><td colSpan={4}>{error ? "Directory data could not be loaded." : "No employees match this search."}</td></tr>}
          </tbody>
        </table>
      </div>
    </section>
  </main>;
}
