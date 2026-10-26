"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";

const API = (process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5080").replace(/\/$/, "");
type Employee = { id: number; employeeNumber: string; fullName: string; email?: string | null; department: string; costCentre: string; status: "Active" | "Inactive" | "Terminated" };

export default function EmployeesPage() {
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [form, setForm] = useState({ employeeNumber: "", fullName: "", email: "", department: "", costCentre: "" });
  const [notice, setNotice] = useState(""); const [error, setError] = useState("");
  const token = () => sessionStorage.getItem("simAccessToken") || "";
  const request = useCallback(async (path: string, init: RequestInit = {}) => {
    const value = token();
    if (!value) { window.location.href = "/login"; throw new Error("Sign in required."); }
    return fetch(API + path, { ...init, headers: { "Content-Type": "application/json", Authorization: "Bearer " + value, ...(init.headers || {}) } });
  }, []);
  const load = useCallback(async () => {
    const response = await request("/api/employees", { cache: "no-store" });
    if (!response.ok) throw new Error("Could not load the employee directory.");
    setEmployees(await response.json() as Employee[]);
  }, [request]);
  useEffect(() => { void load().catch(e => setError(e.message)); }, [load]);

  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setNotice(""); setError("");
    try {
      const response = await request("/api/admin/employees", { method: "POST", body: JSON.stringify(form) });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(body.message || "Administrator permission is required to add employees.");
      setForm({ employeeNumber: "", fullName: "", email: "", department: "", costCentre: "" });
      setNotice("Employee record created."); await load();
    } catch (e) { setError(e instanceof Error ? e.message : "Could not create employee."); }
  }

  async function setStatus(employee: Employee, status: Employee["status"]) {
    setNotice(""); setError("");
    try {
      const response = await request("/api/admin/employees/" + employee.id + "/status", { method: "PUT", body: JSON.stringify({ status }) });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(body.message || "Administrator permission is required.");
      setNotice("Employee status updated."); await load();
    } catch (e) { setError(e instanceof Error ? e.message : "Could not update employee."); }
  }

  return <main className="admin-shell">
    <header className="admin-header"><div><a href="/" className="back-link">← Inventory</a><p className="auth-eyebrow">PEOPLE DIRECTORY</p><h1>Employees</h1><p>Use synthetic employee records for development. Do not import real employee data without authorization.</p></div><button className="text-button" onClick={() => { sessionStorage.removeItem("simAccessToken"); window.location.href = "/login"; }}>Sign out</button></header>
    {notice && <p className="admin-notice">{notice}</p>}{error && <p className="auth-error">{error}</p>}
    <section className="admin-panel"><h2>Add employee</h2><p>Administrator access is required. Employee numbers must be unique.</p>
      <form className="admin-create-form employee-create-form" onSubmit={create}>
        <label>Employee number<input value={form.employeeNumber} onChange={e => setForm({...form, employeeNumber:e.target.value})} required /></label>
        <label>Full name<input value={form.fullName} onChange={e => setForm({...form, fullName:e.target.value})} required /></label>
        <label>Email (optional)<input type="email" value={form.email} onChange={e => setForm({...form, email:e.target.value})} /></label>
        <label>Department<input value={form.department} onChange={e => setForm({...form, department:e.target.value})} required /></label>
        <label>Cost centre<input value={form.costCentre} onChange={e => setForm({...form, costCentre:e.target.value})} required /></label>
        <button className="primary-button" type="submit">Create employee</button>
      </form>
    </section>
    <section className="admin-panel"><h2>Employee records</h2><div className="admin-table-wrap"><table className="admin-table"><thead><tr><th>Employee</th><th>Department</th><th>Cost centre</th><th>Status</th><th>Admin action</th></tr></thead><tbody>
      {employees.map(employee => <tr key={employee.id}><td><strong>{employee.fullName}</strong><small>{employee.employeeNumber}{employee.email ? " · " + employee.email : ""}</small></td><td>{employee.department}</td><td>{employee.costCentre}</td><td>{employee.status}</td><td><select aria-label={"Status for " + employee.employeeNumber} value={employee.status} onChange={e => void setStatus(employee, e.target.value as Employee["status"])}><option>Active</option><option>Inactive</option><option>Terminated</option></select></td></tr>)}
    </tbody></table></div></section>
  </main>;
}
