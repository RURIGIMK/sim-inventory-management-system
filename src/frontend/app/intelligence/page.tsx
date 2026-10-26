"use client";

import { useCallback, useEffect, useState } from "react";

const API = (process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5080").replace(/\/$/, "");

type Alert = {
  code: string;
  severity: "high" | "medium" | "low";
  title: string;
  entityType: string;
  entityId: number;
  entityLabel: string;
  explanation: string;
  observedMonthlyCost?: number | null;
  peerMedianMonthlyCost?: number | null;
  contractEnd?: string | null;
  recommendedAction: string;
};
type Result = {
  evaluatedOn: string;
  simsEvaluated: number;
  alertsRaised: number;
  costComparisonsSkipped: number;
  alerts: Alert[];
};

const money = (value: number) => new Intl.NumberFormat("en-KE", {
  style: "currency", currency: "KES", maximumFractionDigits: 2
}).format(value);

export default function IntelligencePage() {
  const [result, setResult] = useState<Result | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    setError("");
    const token = sessionStorage.getItem("simAccessToken");
    if (!token) {
      window.location.href = "/login";
      return;
    }
    try {
      const response = await fetch(API + "/api/intelligence/alerts", {
        headers: { Authorization: "Bearer " + token },
        cache: "no-store"
      });
      if (response.status === 401) {
        sessionStorage.removeItem("simAccessToken");
        window.location.href = "/login";
        return;
      }
      if (!response.ok) {
        throw new Error(response.status === 403
          ? "Sign in with an approved account to view asset intelligence."
          : "The intelligence service could not be reached. No analysis result has been assumed.");
      }
      setResult(await response.json() as Result);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not load intelligence results.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  return <main className="admin-shell">
    <header className="admin-header">
      <div>
        <a href="/" className="back-link">← Inventory</a>
        <p className="auth-eyebrow">ASSET INTELLIGENCE</p>
        <h1>SIM asset intelligence</h1>
        <p>Explainable review flags for cost outliers, contract dates and inventory consistency.</p>
      </div>
      <button className="text-button" onClick={() => { sessionStorage.removeItem("simAccessToken"); window.location.href = "/login"; }}>Sign out</button>
    </header>

    <section className="intelligence-note">
      <strong>How this works</strong>
      <p>These are deterministic rules, not AI predictions. Every alert includes the condition that triggered it and a recommended human review. The system does not automatically disable SIMs or change account access.</p>
    </section>

    {error && <p className="auth-error" role="alert">{error}</p>}

    {loading ? <section className="admin-panel"><p className="admin-footnote">Evaluating current inventory records…</p></section> :
      result && <>
        <section className="intelligence-metrics">
          <article className="intelligence-metric"><span>SIMs evaluated</span><strong>{result.simsEvaluated}</strong></article>
          <article className="intelligence-metric"><span>Review flags</span><strong>{result.alertsRaised}</strong></article>
          <article className="intelligence-metric"><span>Cost comparisons skipped</span><strong>{result.costComparisonsSkipped}</strong></article>
        </section>
        <section className="admin-panel">
          <div className="audit-summary">
            <div><h2>Review queue</h2><p>Evaluated on {result.evaluatedOn}. Review each flag against source records before acting.</p></div>
            <button className="text-button" onClick={() => void load()}>Re-evaluate</button>
          </div>
          {result.alerts.length === 0 ?
            <div className="intelligence-empty"><strong>No review flags were raised.</strong><p>This does not guarantee that every record is correct. Some rules need enough comparable data before they can evaluate costs.</p></div> :
            <div className="intelligence-list">
              {result.alerts.map((alert, index) => <article className="intelligence-alert" key={alert.code + "-" + alert.entityId + "-" + index}>
                <div className="intelligence-alert-top">
                  <div><span className={"intelligence-severity " + alert.severity}>{alert.severity === "high" ? "High priority" : alert.severity === "medium" ? "Review" : "Informational"}</span><h3>{alert.title}</h3></div>
                  <span className="intelligence-code">{alert.code}</span>
                </div>
                <p className="intelligence-entity">{alert.entityType.toUpperCase()} · {alert.entityLabel} · ID {alert.entityId}</p>
                <p>{alert.explanation}</p>
                {(alert.observedMonthlyCost != null || alert.peerMedianMonthlyCost != null) &&
                  <div className="intelligence-values">
                    {alert.observedMonthlyCost != null && <span>Observed monthly cost <strong>{money(alert.observedMonthlyCost)}</strong></span>}
                    {alert.peerMedianMonthlyCost != null && <span>Peer median <strong>{money(alert.peerMedianMonthlyCost)}</strong></span>}
                  </div>}
                {alert.contractEnd && <p className="intelligence-contract">Contract end: <strong>{alert.contractEnd}</strong></p>}
                <div className="intelligence-action"><strong>Suggested review</strong><p>{alert.recommendedAction}</p></div>
              </article>)}
            </div>}
          <p className="admin-footnote">Cost outlier checks are skipped when fewer than four same-operator peer SIMs are available. Alerts are decision support only and may include false positives.</p>
        </section>
      </>}
  </main>;
}
