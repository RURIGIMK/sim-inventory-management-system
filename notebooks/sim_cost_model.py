"""Reproducible SIM monthly-cost anomaly experiment.

Use synthetic data by default. A CSV may be supplied only when its use is
explicitly authorized and the file has been de-identified.
Required CSV columns: monthly_cost, expected_cost.
Optional label column: known_synthetic_anomaly (0 = normal, 1 = anomaly).
"""

from __future__ import annotations

import argparse
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

import joblib
import numpy as np
import pandas as pd
from sklearn.ensemble import IsolationForest
from sklearn.metrics import confusion_matrix, f1_score, precision_score, recall_score
from sklearn.model_selection import train_test_split

FEATURES = ["monthly_cost", "expected_cost", "cost_ratio"]
LABEL_COLUMN = "known_synthetic_anomaly"
MODEL_VERSION = "1.1.0"


def generate_synthetic_data(
    n_records: int = 600,
    anomaly_rate: float = 0.05,
    seed: int = 42,
) -> pd.DataFrame:
    """Generate controlled synthetic records with non-trivial anomaly ranges."""
    if n_records < 20:
        raise ValueError("n_records must be at least 20.")
    if not 0.01 <= anomaly_rate <= 0.30:
        raise ValueError("anomaly_rate must be between 0.01 and 0.30.")

    rng = np.random.default_rng(seed)
    operators = rng.choice(["Operator A", "Operator B", "Operator C"], size=n_records)
    plans = rng.choice(
        ["Voice", "Data", "Voice + Data"],
        size=n_records,
        p=[0.25, 0.35, 0.40],
    )
    expected_by_plan = {"Voice": 900.0, "Data": 1400.0, "Voice + Data": 2300.0}
    expected_cost = np.array([expected_by_plan[plan] for plan in plans])
    monthly_cost = rng.normal(expected_cost, expected_cost * 0.12)
    monthly_cost = np.maximum(100.0, monthly_cost)

    anomaly_count = max(1, round(n_records * anomaly_rate))
    anomaly_indices = rng.choice(n_records, size=anomaly_count, replace=False)
    labels = np.zeros(n_records, dtype=int)
    labels[anomaly_indices] = 1

    # Deliberately overlap the normal range so the simple ratio baseline is
    # not guaranteed to identify every injected anomaly.
    high_count = (anomaly_count + 1) // 2
    high_indices = anomaly_indices[:high_count]
    low_indices = anomaly_indices[high_count:]
    monthly_cost[high_indices] *= rng.uniform(1.45, 2.20, size=len(high_indices))
    monthly_cost[low_indices] *= rng.uniform(0.45, 0.75, size=len(low_indices))

    frame = pd.DataFrame({
        "synthetic_sim_id": [f"SIM-DEMO-{i + 1:05d}" for i in range(n_records)],
        "operator": operators,
        "plan_type": plans,
        "expected_cost": expected_cost,
        "monthly_cost": np.round(monthly_cost, 2),
        LABEL_COLUMN: labels,
    })
    frame["cost_ratio"] = frame["monthly_cost"] / frame["expected_cost"]
    return frame


def load_dataset(input_csv: Path | None, args: argparse.Namespace) -> tuple[pd.DataFrame, str]:
    if input_csv is None:
        return generate_synthetic_data(args.records, args.anomaly_rate, args.seed), "synthetic"

    frame = pd.read_csv(input_csv)
    required = {"monthly_cost", "expected_cost"}
    missing = required.difference(frame.columns)
    if missing:
        raise ValueError(f"CSV is missing required columns: {', '.join(sorted(missing))}")

    for column in ("monthly_cost", "expected_cost"):
        frame[column] = pd.to_numeric(frame[column], errors="coerce")
    if frame[["monthly_cost", "expected_cost"]].isna().any().any():
        raise ValueError("Cost columns contain missing or non-numeric values.")
    if not np.isfinite(frame[["monthly_cost", "expected_cost"]].to_numpy()).all():
        raise ValueError("Cost columns must contain finite numeric values.")
    if (frame["monthly_cost"] < 0).any() or (frame["expected_cost"] <= 0).any():
        raise ValueError("monthly_cost must be >= 0 and expected_cost must be > 0.")
    if len(frame) < 20:
        raise ValueError("At least 20 valid records are required.")

    if LABEL_COLUMN in frame.columns:
        labels = pd.to_numeric(frame[LABEL_COLUMN], errors="raise")
        if not labels.isin([0, 1]).all():
            raise ValueError(f"{LABEL_COLUMN} must contain only 0 and 1.")
        frame[LABEL_COLUMN] = labels.astype(int)

    frame["cost_ratio"] = frame["monthly_cost"] / frame["expected_cost"]
    return frame, "approved_csv"


def metric_summary(y_true: pd.Series, predictions: np.ndarray) -> dict:
    matrix = confusion_matrix(y_true, predictions, labels=[0, 1])
    tn, fp, fn, tp = (int(value) for value in matrix.ravel())
    return {
        "records_evaluated": int(len(y_true)),
        "true_negatives": tn,
        "false_positives": fp,
        "false_negatives": fn,
        "true_positives": tp,
        "precision": float(precision_score(y_true, predictions, zero_division=0)),
        "recall": float(recall_score(y_true, predictions, zero_division=0)),
        "f1": float(f1_score(y_true, predictions, zero_division=0)),
        "false_positive_rate": float(fp / (fp + tn)) if (fp + tn) else 0.0,
    }


def run_experiment(args: argparse.Namespace) -> dict:
    model_dir = Path(args.model_dir)
    results_dir = Path(args.results_dir)
    model_dir.mkdir(parents=True, exist_ok=True)
    results_dir.mkdir(parents=True, exist_ok=True)
    frame, source = load_dataset(Path(args.input_csv) if args.input_csv else None, args)

    features = frame[FEATURES].astype(float)
    has_labels = LABEL_COLUMN in frame.columns
    if has_labels:
        labels = frame[LABEL_COLUMN].astype(int)
        if labels.value_counts().min() < 2:
            raise ValueError("At least two records per class are required for a stratified evaluation split.")
        if labels.nunique() < 2:
            raise ValueError("Evaluation requires both normal (0) and anomaly (1) labels.")
        train_idx, test_idx = train_test_split(
            frame.index,
            test_size=args.test_size,
            random_state=args.seed,
            stratify=labels,
        )
        train_features = features.loc[train_idx]
        test_features = features.loc[test_idx]
        y_test = labels.loc[test_idx]
    else:
        train_idx = frame.index
        test_idx = frame.index
        train_features = features
        test_features = features
        y_test = None

    model = IsolationForest(
        n_estimators=args.n_estimators,
        contamination=args.contamination,
        random_state=args.seed,
    )
    model.fit(train_features)

    predictions = (model.predict(test_features) == -1).astype(int)
    scores = -model.decision_function(test_features)

    evaluation = frame.loc[test_idx].copy()
    evaluation["isolation_forest_flag"] = predictions
    evaluation["anomaly_score"] = scores

    metrics = None
    if has_labels and y_test is not None:
        # Transparent reference rule, deliberately independent of model fitting.
        ratios = evaluation["cost_ratio"]
        baseline_predictions = ((ratios > 1.8) | (ratios < 0.35)).astype(int).to_numpy()
        evaluation["baseline_flag"] = baseline_predictions
        evaluation["isolation_forest_result"] = np.select(
            [
                (y_test.to_numpy() == 0) & (predictions == 1),
                (y_test.to_numpy() == 1) & (predictions == 0),
            ],
            ["False positive", "Missed anomaly"],
            default="Correct",
        )
        evaluation["baseline_result"] = np.select(
            [
                (y_test.to_numpy() == 0) & (baseline_predictions == 1),
                (y_test.to_numpy() == 1) & (baseline_predictions == 0),
            ],
            ["False positive", "Missed anomaly"],
            default="Correct",
        )
        metrics = {
            "baseline": metric_summary(y_test, baseline_predictions),
            "isolation_forest": metric_summary(y_test, predictions),
        }

    evaluation_path = results_dir / "sim_anomaly_evaluation.csv"
    evaluation.to_csv(evaluation_path, index=False)

    model_path = model_dir / "sim_cost_isolation_forest.joblib"
    joblib.dump(model, model_path)

    dataset_bytes = frame.to_csv(index=False).encode("utf-8")
    metadata = {
        "model_name": "SIM Cost Isolation Forest",
        "model_version": MODEL_VERSION,
        "model_type": "IsolationForest",
        "created_at_utc": datetime.now(timezone.utc).isoformat(),
        "training_data_source": source,
        "data_classification": (
            "Synthetic demonstration data only"
            if source == "synthetic"
            else "Caller-confirmed approved and de-identified input; verify authorization"
        ),
        "dataset_records": int(len(frame)),
        "training_records": int(len(train_idx)),
        "test_records": int(len(test_idx)) if has_labels else 0,
        "features": FEATURES,
        "n_estimators": args.n_estimators,
        "contamination": args.contamination,
        "random_state": args.seed,
        "scikit_learn_version": __import__("sklearn").__version__,
        "dataset_sha256": hashlib.sha256(dataset_bytes).hexdigest(),
        "evaluation_metrics": metrics,
        "limitations": [
            "Synthetic results do not establish real-world predictive performance.",
            "Unlabelled input produces no precision, recall, F1 or false-positive-rate claims.",
            "Anomaly flags require human review and must not trigger automatic operational action.",
        ],
    }
    metadata_path = model_dir / "sim_cost_model_metadata.json"
    metadata_path.write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")

    summary = {
        "model_path": str(model_path),
        "metadata_path": str(metadata_path),
        "evaluation_path": str(evaluation_path),
        "metadata": metadata,
    }
    (results_dir / "run_summary.json").write_text(
        json.dumps(summary, indent=2) + "\n", encoding="utf-8"
    )
    return summary


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input-csv", default=None, help="Optional approved, de-identified CSV.")
    parser.add_argument("--model-dir", default="notebooks/models", help="Directory for the model and metadata.")
    parser.add_argument("--results-dir", default="notebooks/results", help="Directory for evaluation outputs.")
    parser.add_argument("--records", type=int, default=600, help="Synthetic record count.")
    parser.add_argument("--anomaly-rate", type=float, default=0.05)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--test-size", type=float, default=0.30)
    parser.add_argument("--n-estimators", type=int, default=250)
    parser.add_argument("--contamination", type=float, default=0.05)
    args = parser.parse_args()
    if not 0.1 <= args.test_size <= 0.5:
        parser.error("--test-size must be between 0.1 and 0.5.")
    if not 0.01 <= args.contamination <= 0.30:
        parser.error("--contamination must be between 0.01 and 0.30.")
    return args


if __name__ == "__main__":
    result = run_experiment(parse_args())
    metadata = result["metadata"]
    print(f"Model written to: {result['model_path']}")
    print(f"Metadata written to: {result['metadata_path']}")
    print(f"Evaluation written to: {result['evaluation_path']}")
    if metadata["evaluation_metrics"]:
        print(json.dumps(metadata["evaluation_metrics"], indent=2))
    else:
        print("No labelled evaluation metrics: input dataset has no anomaly labels.")
