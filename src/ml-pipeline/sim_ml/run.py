"""Scheduled ML run: fetch → features → evaluate → register candidate → score with candidate and active model.

    python -m sim_ml.run --api-base https://api.example --dry-run      # evaluate only, change nothing
    python -m sim_ml.run --api-base https://api.example                # register a candidate and publish scores
    python -m sim_ml.run --dataset-json saved.json --dry-run           # offline, e.g. in Colab

The integration token is read from SIM_INTEGRATION_TOKEN. A registered model is only a candidate:
an administrator activates it in the Intelligence page after comparing it with the rule and the active model.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import subprocess
import sys
from pathlib import Path

import numpy as np
import pandas as pd
import sklearn

from . import model as m
from .client import ApiClient
from .features import MODEL_FEATURES, build_frame

MIN_ROWS = 60
MIN_MONTHS = 6


def choose_labels(frame: pd.DataFrame, min_reviews: int) -> str | None:
    """Human reviews win once there are enough of both kinds; synthetic labels are a stand-in until then."""
    reviews = frame["label_review"].dropna()
    if len(reviews) >= min_reviews and reviews.nunique() == 2:
        return "label_review"
    synthetic = frame["label_synthetic"].dropna()
    if len(synthetic) and synthetic.nunique() == 2:
        return "label_synthetic"
    return None


def score_rows(bundle: m.Bundle, frame: pd.DataFrame) -> list[dict]:
    scores = bundle.score(frame)
    flags = scores >= bundle.threshold
    out = []
    for position, (_, row) in enumerate(frame.iterrows()):
        out.append({
            "simId": int(row["simId"]), "period": row["periodText"], "score": round(float(scores[position]), 6),
            "isFlagged": bool(flags[position]),
            "reasons": m.explain(bundle, row) if flags[position] else [],
        })
    return out


def git_commit() -> str | None:
    if os.environ.get("GITHUB_SHA"):
        return os.environ["GITHUB_SHA"]
    try:
        return subprocess.run(["git", "rev-parse", "HEAD"], capture_output=True, text=True, check=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return None


def run(args: argparse.Namespace) -> dict:
    client = None
    if args.dataset_json:
        dataset = json.loads(Path(args.dataset_json).read_text(encoding="utf-8"))
    else:
        client = ApiClient(args.api_base, os.environ.get("SIM_INTEGRATION_TOKEN", ""))
        client.wait_until_healthy()
        dataset = client.dataset()
        if args.save_dataset:
            Path(args.save_dataset).write_text(json.dumps(dataset), encoding="utf-8")

    frame = build_frame(dataset)
    months = frame["period"].nunique() if not frame.empty else 0
    summary: dict = {"rows": int(len(frame)), "months": int(months), "sims": int(frame["simId"].nunique()) if len(frame) else 0}
    if len(frame) < MIN_ROWS or months < MIN_MONTHS:
        summary["status"] = f"skipped: need at least {MIN_ROWS} SIM-months over {MIN_MONTHS} months of billing history"
        return summary

    active_meta, active_bundle = None, None
    if client is not None:
        active_meta = next((x for x in client.models() if x["status"] == "active"), None)
        if active_meta:
            _, artifact = client.artifact(active_meta["id"])
            active_bundle = m.Bundle.from_bytes(artifact)

    label_column = choose_labels(frame, args.min_reviews)
    evaluation = (m.evaluate(frame, label_column, args.test_months, args.flag_rate, active_bundle)
                  if label_column else {"evaluated": False, "reason": "No labels yet: review flags to enable evaluation."})
    summary["evaluation"] = evaluation

    final = m.fit(frame, args.flag_rate)  # same procedure, refit on every month for scoring new data
    dataset_bytes = json.dumps({"costs": dataset.get("costs"), "reviews": dataset.get("reviews")}, sort_keys=True).encode()
    now = dt.datetime.now(dt.timezone.utc)
    commit = git_commit()
    version = now.strftime("%Y.%m.%d-%H%M") + (f"-{commit[:7]}" if commit else "")
    metrics = {
        "evaluation": evaluation, "flag_rate": args.flag_rate, "threshold": final.threshold,
        "label_source": {"label_review": "human_reviews", "label_synthetic": "synthetic_scenarios"}.get(label_column or "", "none"),
        "scikit_learn": sklearn.__version__, "data_classification":
            "synthetic" if (frame["source"] == "synthetic").all() else
            "mixed" if (frame["source"] == "synthetic").any() else "operator_invoice",
    }
    labelled = int(frame[label_column].notna().sum()) if label_column else 0
    summary.update({"version": version, "label_source": metrics["label_source"], "labelled_rows": labelled})

    if args.dry_run or client is None:
        summary["status"] = "dry run: nothing registered or published"
        summary["flagged_by_candidate"] = int(final.flags(frame).sum())
        return summary

    registered = client.register({
        "version": version, "algorithm": m.ALGORITHM, "trainedAt": now.isoformat(), "trainingRows": int(len(frame)),
        "labelledRows": labelled, "labelSource": metrics["label_source"],
        "datasetSha256": hashlib.sha256(dataset_bytes).hexdigest(), "features": MODEL_FEATURES,
        "metrics": json.loads(json.dumps(metrics, default=float)),
        "artifactBase64": __import__("base64").b64encode(final.to_bytes()).decode(), "gitCommit": commit,
    })
    summary["registered"] = registered
    summary["published_candidate"] = client.publish_scores(registered["id"], score_rows(final, frame))
    if active_meta and active_bundle is not None:
        try:
            summary["published_active"] = client.publish_scores(active_meta["id"], score_rows(active_bundle, frame))
        except KeyError:
            summary["published_active"] = "skipped: active model expects different features"
    summary["status"] = "candidate registered; an administrator decides whether to activate it"
    return summary


def markdown(summary: dict) -> str:
    lines = ["## SIM cost anomaly pipeline", "", f"**Status:** {summary.get('status')}", "",
             f"Rows: {summary.get('rows')} · months: {summary.get('months')} · SIMs: {summary.get('sims')}"]
    ev = summary.get("evaluation") or {}
    if ev.get("evaluated"):
        lines += ["", f"Held-out evaluation on the last {ev['test_months']} months "
                      f"({ev['test_rows']} rows, {ev['test_positives']} positives, labels: {ev['label_column']}):", "",
                  "| Method | Flagged | Precision | Recall | F1 | False-positive rate |", "|---|---|---|---|---|---|"]
        for key, name in [("candidate", "Candidate model"), ("active", "Active model"),
                          ("rule", "Rule (≥1.5× or ≤0.5× contract)"), ("ratio_ranked_same_budget", "Ratio ranking, same budget")]:
            r = ev.get(key)
            if r and "precision" in r:
                lines.append(f"| {name} | {r['flagged']} | {r['precision']:.2f} | {r['recall']:.2f} | {r['f1']:.2f} | {r['false_positive_rate']:.3f} |")
        if ev.get("recall_by_scenario"):
            lines += ["", "| Synthetic scenario | Rows | Caught by candidate | Caught by rule |", "|---|---|---|---|"]
            for name, r in ev["recall_by_scenario"].items():
                lines.append(f"| {name} | {r['rows']} | {r['caught_by_candidate']} | {r['caught_by_rule']} |")
    elif ev:
        lines += ["", f"Evaluation: {ev.get('reason')}"]
    return "\n".join(lines) + "\n"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--api-base", default=os.environ.get("SIM_API_BASE_URL"))
    parser.add_argument("--dataset-json", help="Run offline from a saved dataset instead of the API.")
    parser.add_argument("--save-dataset", help="Also save the fetched dataset to this path.")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--test-months", type=int, default=5)
    parser.add_argument("--flag-rate", type=float, default=0.05)
    parser.add_argument("--min-reviews", type=int, default=30)
    parser.add_argument("--summary-json", help="Write the run summary here.")
    args = parser.parse_args(argv)
    if not args.dataset_json and not args.api_base:
        parser.error("--api-base (or SIM_API_BASE_URL) is required unless --dataset-json is given")
    if not 0.005 <= args.flag_rate <= 0.3:
        parser.error("--flag-rate must be between 0.005 and 0.3")

    summary = run(args)
    print(json.dumps(summary, indent=2, default=str))
    if args.summary_json:
        Path(args.summary_json).write_text(json.dumps(summary, indent=2, default=str), encoding="utf-8")
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as handle:
            handle.write(markdown(summary))
    return 0


if __name__ == "__main__":
    sys.exit(main())
