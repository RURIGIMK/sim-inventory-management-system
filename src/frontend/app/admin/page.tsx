"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";

const API = (process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5080").replace(/\/$/, "");
type User = { id: string; email: string; displayName: string; role: "Administrator" | "DepartmentalManager" | "Viewer"; isActive: boolean; totpEnabled: boolean; createdAt: string };

export default function AdminUsersPage() {
  const [users, setUsers] = useState<User[]>([]);
  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [role, setRole] = useState<User["role"]>("Viewer");
  const [notice, setNotice] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const authHeaders = useCallback(() => {
    const token = sessionStorage.getItem("simAccessToken");
    if (!token) { window.location.href = "/login"; return {}; }
    return { Authorization: "Bearer " + token, "Content-Type": "application/json" };
  }, []);

  const loadUsers = useCallback(async () => {
    const response = await fetch(API + "/api/admin/users", { headers: authHeaders(), cache: "no-store" });
    if (response.status === 401 || response.status === 403) {
      setError("Administrator access is required. Sign in with an active administrator account.");
      if (response.status === 401) { sessionStorage.removeItem("simAccessToken"); window.location.href = "/login"; }
      return;
    }
    if (!response.ok) throw new Error("Could not load accounts.");
    setUsers(await response.json() as User[]);
  }, [authHeaders]);

  useEffect(() => { void loadUsers().catch(e => setError(e.message)); }, [loadUsers]);

  async function createUser(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError(""); setNotice("");
    try {
      const response = await fetch(API + "/api/admin/users", {
        method: "POST", headers: authHeaders(),
        body: JSON.stringify({ email, displayName, role })
      });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(body.message || "Could not create account.");
      setEmail(""); setDisplayName(""); setRole("Viewer");
      setNotice("Account provisioned. The user can sign in with this exact Google email and will be asked to enrol in authenticator MFA.");
      await loadUsers();
    } catch (e) { setError(e instanceof Error ? e.message : "Could not create account."); }
    finally { setBusy(false); }
  }

  async function updateUser(user: User, nextRole: User["role"], nextActive: boolean) {
    setError(""); setNotice("");
    try {
      const response = await fetch(API + "/api/admin/users/" + user.id, {
        method: "PUT", headers: authHeaders(),
        body: JSON.stringify({ role: nextRole, isActive: nextActive })
      });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(body.message || "Could not update account.");
      setNotice("Account settings updated."); await loadUsers();
    } catch (e) { setError(e instanceof Error ? e.message : "Could not update account."); }
  }

  return <main className="admin-shell">
    <header className="admin-header">
      <div><a href="/" className="back-link">← Inventory</a><p className="auth-eyebrow">ACCESS CONTROL</p><h1>User administration</h1><p>Create approved accounts and change access without editing application code.</p></div>
      <button className="text-button" onClick={() => { sessionStorage.removeItem("simAccessToken"); window.location.href = "/login"; }}>Sign out</button>
    </header>
    {notice && <p className="admin-notice" role="status">{notice}</p>}
    {error && <p className="auth-error" role="alert">{error}</p>}
    <section className="admin-panel">
      <h2>Provision an account</h2>
      <p>The email must match the Google account the user will use. No password is created or stored by this application.</p>
      <form onSubmit={createUser} className="admin-create-form">
        <label>Full name<input value={displayName} onChange={e => setDisplayName(e.target.value)} required /></label>
        <label>Google account email<input type="email" value={email} onChange={e => setEmail(e.target.value)} required /></label>
        <label>Role<select value={role} onChange={e => setRole(e.target.value as User["role"])}><option value="Viewer">Viewer</option><option value="DepartmentalManager">Departmental Manager</option><option value="Administrator">Administrator</option></select></label>
        <button className="primary-button" type="submit" disabled={busy}>{busy ? "Creating…" : "Create account"}</button>
      </form>
    </section>
    <section className="admin-panel">
      <h2>Existing accounts</h2>
      <div className="admin-table-wrap"><table className="admin-table"><thead><tr><th>Account</th><th>Role</th><th>Status</th><th>MFA</th><th>Actions</th></tr></thead>
        <tbody>{users.map(user => <tr key={user.id}>
          <td><strong>{user.displayName}</strong><small>{user.email}</small></td>
          <td><select aria-label={"Role for " + user.email} value={user.role} onChange={e => void updateUser(user, e.target.value as User["role"], user.isActive)}><option value="Viewer">Viewer</option><option value="DepartmentalManager">Departmental Manager</option><option value="Administrator">Administrator</option></select></td>
          <td><span className={user.isActive ? "account-active" : "account-disabled"}>{user.isActive ? "Active" : "Disabled"}</span></td>
          <td>{user.totpEnabled ? "Enabled" : "Pending"}</td>
          <td><button className="text-button" onClick={() => void updateUser(user, user.role, !user.isActive)}>{user.isActive ? "Disable" : "Enable"}</button></td>
        </tr>)}</tbody>
      </table></div>
    </section>
    <p className="admin-footnote">Account changes are recorded in the append-only, hash-linked audit log. Disabled users are rejected by the API, including when they still hold a previously issued token.</p>
  </main>;
}
