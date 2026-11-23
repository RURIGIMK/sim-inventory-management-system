"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

const API = (process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5080").replace(/\/$/, "");

type Metric = { flagged: number; precision: number; recall: number; f1: number; false_positive_rate: number; average_precision?: number };
type Evaluation = {
  evaluated: boolean; reason?: string; label_column?: string; test_months?: number; test_rows?: number; test_positives?: number;
  candidate?: Metric; active?: Metric; rule?: Metric; ratio_ranked_same_budget?: Metric;
  recall_by_scenario?: Record<string, { rows: number; caught_by_candidate: number; caught_by_rule: number }>;
};
type ModelInfo = {
  id: number; version: string; status: string; algorithm: string; trainedAt: string; trainingRows: number;
  labelledRows: number; labelSource: string; shadow: boolean; metrics: { evaluation?: Evaluation; data_classification?: string };
};
type Item = {
  simId: number; phoneNumber: string; operator: string; planType: string; costCentre: string; period: string;
  amount: number; contracted: number; ratio: number | null; dataUsageMb: number | null; voiceMinutes: number | null;
  synthetic: boolean; ruleFlag: boolean; ruleReason: string | null; modelScore: number | null; modelFlag: boolean;
  modelReasons: string[]; review: { decision: string; note: string; reviewedAt: string; flaggedBy: string } | null;
};
type Perf = { confirmedFlags: number; dismissedFlags: number; missedConfirmed: number; precision: number | null; recall: number | null };
type Response = {
  periods: string[]; selectedPeriod: string | null; model: ModelInfo | null; items: Item[];
  performance: { reviewedSimMonths: number; confirmed: number; rule: Perf; model: Perf | null } | null;
};
type Version = { id: number; version: string; status: string; trainedAt: string; activatedAt: string | null; labelSource: string };

const money = (value: number) => new Intl.NumberFormat("en-KE", { style: "currency", currency: "KES", maximumFractionDigits: 0 }).format(value);
const pct = (value: number | null | undefined) => value == null ? "—" : `${Math.round(value * 100)}%`;
const scenarioNames: Record<string, string> = {
  roaming_spike: "Roaming spike", plan_mismatch: "Plan too small for usage",
  dormant_billed: "Billed while unused", duplicate_charge: "Charged twice"
};

export default function CostAnomalies({ isAdmin }: { isAdmin: boolean }) {
  const [data, setData] = useState<Response | null>(null);
  const [versions, setVersions] = useState<Version[]>([]);
  const [period, setPeriod] = useState("");
  const [flaggedOnly, setFlaggedOnly] = useState(true);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const request = useCallback((path: string, init: RequestInit = {}) => {
    const token = sessionStorage.getItem("simAccessToken") || "";
    return fetch(API + path, { ...init, cache: "no-store", headers: { "Content-Type": "application/json", Authorization: "Bearer " + token, ...(init.headers || {}) } });
  }, []);

  const load = useCallback(async (selected?: string) => {
    setError("");
    try {
      const [anomalies, models] = await Promise.all([
        request("/api/intelligence/anomalies" + (selected ? `?period=${selected}` : "")),
        request("/api/ml/models")
      ]);
      if (!anomalies.ok) throw new Error("Cost anomaly results could not be loaded.");
      const body = await anomalies.json() as Response;
      setData(body);
      setPeriod(body.selectedPeriod ?? "");
      if (models.ok) setVersions(await models.json() as Version[]);
    } catch (e) { setError(e instanceof Error ? e.message : "Cost anomaly results could not be loaded."); }
  }, [request]);

  useEffect(() => { void load(); }, [load]);

  async function review(item: Item, decision: "confirmed" | "dismissed") {
    const note = window.prompt(decision === "confirmed"
      ? "What did you find? (e.g. invoice shows roaming charges)" : "Why is this charge valid? (e.g. approved travel)") ?? "";
    setBusy(true);
    try {
      const response = await request("/api/intelligence/reviews", { method: "POST", body: JSON.stringify({ simId: item.simId, period: item.period, decision, note }) });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(body.message || "The review was not saved.");
      await load(period);
    } catch (e) { setError(e instanceof Error ? e.message : "The review was not saved."); }
    finally { setBusy(false); }
  }

  async function activate(version: Version) {
    if (!window.confirm(`Activate model ${version.version}? Its flags will replace the current model's flags on this page. You can roll back by activating a previous version.`)) return;
    setBusy(true);
    try {
      const response = await request(`/api/ml/models/${version.id}/activate`, { method: "POST" });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(body.message || "The model was not activated.");
      await load(period);
    } catch (e) { setError(e instanceof Error ? e.message : "The model was not activated."); }
    finally { setBusy(false); }
  }

  const items = useMemo(() => (data?.items ?? []).filter(i => !flaggedOnly || i.ruleFlag || i.modelFlag || i.review),
    [data, flaggedOnly]);
  const ev = data?.model?.metrics?.evaluation;

  return <section className="admin-panel">
    <div className="audit-summary">
      <div>
        <h2>Cost anomalies by month</h2>
        <p>Each billed month is checked two ways: a fixed rule (billed ≥1.5× or ≤0.5× the contract) and a model that also looks at the SIM&apos;s own history, its peers and its usage. Both are prompts for a human, never decisions.</p>
      </div>
      {data && data.periods.length > 0 && <label>Month <select value={period} onChange={e => void load(e.target.value)}>{data.periods.map(p => <option key={p}>{p}</option>)}</select></label>}
    </div>
    {error && <p className="auth-error" role="alert">{error}</p>}

    {data && !data.model && <p className="intelligence-note">No model has been trained yet. The nightly ML pipeline registers one once there are at least six months of billing history. Until then only the rule runs.</p>}

    {data?.model && <div className="intelligence-note">
      <strong>Model {data.model.version} · {data.model.shadow ? "candidate awaiting review (shown in shadow mode)" : "active"}</strong>
      <p>{data.model.algorithm}. Trained {new Date(data.model.trainedAt).toLocaleString("en-KE")} on {data.model.trainingRows} SIM-months
        ({data.model.metrics?.data_classification ?? "unknown"} data). Evaluation labels: {data.model.labelSource === "human_reviews" ? "your reviews" : data.model.labelSource === "synthetic_scenarios" ? "synthetic scenarios (until enough reviews exist)" : "none yet"}.</p>
      {ev?.evaluated ? <>
        <p>Tested on the {ev.test_months} most recent months, which the model did not see in training ({ev.test_rows} SIM-months, {ev.test_positives} known anomalies):</p>
        <div className="admin-table-wrap"><table className="admin-table">
          <thead><tr><th>Method</th><th>Flags</th><th>Precision</th><th>Recall</th><th>False alarms</th></tr></thead>
          <tbody>
            {([["Candidate model", ev.candidate], ["Active model", ev.active], ["Rule", ev.rule], ["Ratio ranking, same number of flags", ev.ratio_ranked_same_budget]] as [string, Metric | undefined][])
              .filter(([, m]) => m && m.precision !== undefined)
              .map(([name, m]) => <tr key={name}><td>{name}</td><td>{m!.flagged}</td><td>{pct(m!.precision)}</td><td>{pct(m!.recall)}</td><td>{pct(m!.false_positive_rate)}</td></tr>)}
          </tbody>
        </table></div>
        {ev.recall_by_scenario && <p>By scenario: {Object.entries(ev.recall_by_scenario).map(([k, v]) =>
          `${scenarioNames[k] ?? k}: model ${v.caught_by_candidate}/${v.rows}, rule ${v.caught_by_rule}/${v.rows}`).join(" · ")}</p>}
      </> : <p>{ev?.reason ?? "No evaluation yet."}</p>}
    </div>}

    {data?.performance && data.performance.reviewedSimMonths > 0 && <div className="intelligence-metrics">
      <article className="intelligence-metric"><span>Reviewed SIM-months</span><strong>{data.performance.reviewedSimMonths}</strong></article>
      <article className="intelligence-metric"><span>Rule precision / recall on reviews</span><strong>{pct(data.performance.rule.precision)} / {pct(data.performance.rule.recall)}</strong></article>
      {data.performance.model && <article className="intelligence-metric"><span>Model precision / recall on reviews</span><strong>{pct(data.performance.model.precision)} / {pct(data.performance.model.recall)}</strong></article>}
    </div>}

    <label><input type="checkbox" checked={flaggedOnly} onChange={e => setFlaggedOnly(e.target.checked)} /> Show only flagged or reviewed SIMs</label>
    <div className="admin-table-wrap"><table className="admin-table">
      <thead><tr><th>SIM</th><th>Billed vs contract</th><th>Usage</th><th>Rule</th><th>Model</th><th>Review</th></tr></thead>
      <tbody>
        {items.map(item => <tr key={item.simId}>
          <td><strong>{item.phoneNumber}</strong><small>{item.operator} · {item.planType} · {item.costCentre}</small></td>
          <td>{money(item.amount)} / {money(item.contracted)}<small>{item.ratio != null ? `${item.ratio.toFixed(2)}×` : ""}{item.synthetic ? " · synthetic" : ""}</small></td>
          <td>{item.dataUsageMb ?? "—"} MB<small>{item.voiceMinutes ?? "—"} min</small></td>
          <td>{item.ruleFlag ? <><strong>Flagged</strong><small>{item.ruleReason}</small></> : "—"}</td>
          <td>{item.modelFlag ? <><strong>Flagged</strong>{item.modelReasons.map(r => <small key={r}>{r}</small>)}</> : item.modelScore != null ? "—" : "not scored"}</td>
          <td>{item.review
            ? <><strong>{item.review.decision === "confirmed" ? "Confirmed" : "Dismissed"}</strong><small>{item.review.note}</small></>
            : isAdmin && (item.ruleFlag || item.modelFlag || !flaggedOnly)
              ? <><button className="text-button" disabled={busy} onClick={() => void review(item, "confirmed")}>Confirm</button> <button className="text-button" disabled={busy} onClick={() => void review(item, "dismissed")}>Dismiss</button></>
              : "—"}</td>
        </tr>)}
        {data && items.length === 0 && <tr><td colSpan={6}>{data.periods.length === 0 ? "No billing history yet. Import an operator invoice on the Billing page." : "Nothing flagged for this month."}</td></tr>}
      </tbody>
    </table></div>
    <p className="admin-footnote">Reviews are the real labels: once enough exist, the pipeline measures every new model against them instead of synthetic scenarios, and the precision shown above comes only from your decisions. A model flag is not proof of overbilling or misuse.</p>

    {versions.length > 0 && <>
      <h3>Model versions</h3>
      <div className="admin-table-wrap"><table className="admin-table">
        <thead><tr><th>Version</th><th>Status</th><th>Trained</th><th>Labels</th><th /></tr></thead>
        <tbody>{versions.map(v => <tr key={v.id}>
          <td><code>{v.version}</code></td><td>{v.status}{v.activatedAt ? <small>activated {new Date(v.activatedAt).toLocaleDateString("en-KE")}</small> : null}</td>
          <td>{new Date(v.trainedAt).toLocaleString("en-KE")}</td><td>{v.labelSource}</td>
          <td>{isAdmin && v.status !== "active" && <button className="text-button" disabled={busy} onClick={() => void activate(v)}>{v.status === "candidate" ? "Activate" : "Roll back to this"}</button>}</td>
        </tr>)}</tbody>
      </table></div>
      <p className="admin-footnote">The pipeline only proposes models. Activation is an administrator decision recorded in the audit log.</p>
    </>}
  </section>;
}
