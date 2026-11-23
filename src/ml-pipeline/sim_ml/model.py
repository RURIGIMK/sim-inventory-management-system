"""Training, time-ordered evaluation, explanation and safe serialisation of the anomaly model."""
from __future__ import annotations

import math
from dataclasses import dataclass

import numpy as np
import pandas as pd
import skops.io as sio
from sklearn.ensemble import IsolationForest
from sklearn.metrics import average_precision_score

from .features import MODEL_FEATURES

# Transparent baseline, identical to CostIntelligenceEndpoints.RuleHighRatio/RuleLowRatio in the API.
RULE_HIGH = 1.5
RULE_LOW = 0.5
ALGORITHM = "IsolationForest(n_estimators=300) on 9 billing-history features"
# skops refuses to load any type not listed here, so a tampered artifact cannot run arbitrary code.
TRUSTED_TYPES = ["sklearn.tree._tree.Tree"]


@dataclass
class Bundle:
    model: IsolationForest
    features: list[str]
    threshold: float
    median: np.ndarray
    scale: np.ndarray

    def to_bytes(self) -> bytes:
        return sio.dumps({
            "model": self.model, "features": list(self.features), "threshold": float(self.threshold),
            "median": self.median, "scale": self.scale,
        })

    @staticmethod
    def from_bytes(data: bytes) -> "Bundle":
        untrusted = [t for t in sio.get_untrusted_types(data=data) if t not in TRUSTED_TYPES]
        if untrusted:
            raise RuntimeError(f"Refusing to load model artifact with unexpected types: {untrusted}")
        raw = sio.loads(data, trusted=TRUSTED_TYPES)
        return Bundle(raw["model"], raw["features"], raw["threshold"], np.asarray(raw["median"]), np.asarray(raw["scale"]))

    def score(self, frame: pd.DataFrame) -> np.ndarray:
        """Higher = more unusual."""
        return -self.model.decision_function(frame[self.features].astype(float))

    def flags(self, frame: pd.DataFrame) -> np.ndarray:
        return self.score(frame) >= self.threshold


def fit(frame: pd.DataFrame, flag_rate: float, seed: int = 42) -> Bundle:
    x = frame[MODEL_FEATURES].astype(float)
    model = IsolationForest(n_estimators=300, contamination="auto", random_state=seed).fit(x)
    scores = -model.decision_function(x)
    threshold = float(np.quantile(scores, 1.0 - flag_rate))
    median = x.median().to_numpy()
    q75, q25 = x.quantile(0.75).to_numpy(), x.quantile(0.25).to_numpy()
    scale = np.where((q75 - q25) > 1e-6, q75 - q25, x.std(ddof=0).replace(0, 1).to_numpy())
    return Bundle(model, list(MODEL_FEATURES), threshold, median, scale)


def rule_flags(frame: pd.DataFrame) -> np.ndarray:
    ratio = frame["amount"].astype(float) / frame["contracted"].replace(0, np.nan).astype(float)
    return ((ratio >= RULE_HIGH) | (ratio <= RULE_LOW)).fillna(False).to_numpy()


def explain(bundle: Bundle, row: pd.Series, top: int = 2) -> list[str]:
    """Plain-language reasons: the features furthest from normal, in the reviewer's units."""
    values = row[bundle.features].astype(float).to_numpy()
    z = np.abs((values - bundle.median) / bundle.scale)
    reasons: list[str] = []
    for index in np.argsort(-z):
        if z[index] < 1.5 or len(reasons) >= top:
            break
        text = _describe(bundle.features[index], row)
        if text and text not in reasons:
            reasons.append(text)
    return reasons or ["Unusual combination of cost and usage compared with this SIM's history and its peers."]


def _describe(feature: str, row: pd.Series) -> str | None:
    def times(log_value: float) -> float:
        return math.exp(float(log_value))
    if feature == "log_ratio_contract":
        return f"Billed {row['amount'] / max(row['contracted'], 1):.2f}× the contracted monthly fee."
    if feature == "log_ratio_own_history":
        return f"Bill is {times(row[feature]):.2f}× this SIM's usual monthly amount."
    if feature == "log_ratio_previous_month":
        return f"Bill is {times(row[feature]):.2f}× the previous month."
    if feature == "log_ratio_peers":
        return f"Bill is {times(row[feature]):.2f}× similar SIMs on the same operator and plan this month."
    if feature == "log_data_vs_history":
        return f"Data usage is {times(row[feature]):.1f}× its usual level."
    if feature == "log_voice_vs_history":
        return f"Voice usage is {times(row[feature]):.1f}× its usual level."
    if feature in ("zero_usage", "zero_usage_streak") and row["zero_usage_streak"] > 0:
        months = int(row["zero_usage_streak"])
        return f"No data or voice usage for {months} month{'s' if months != 1 else ''} while still being billed."
    if feature == "holder_inactive" and row["holder_inactive"]:
        return "Held by someone the HR directory no longer lists as active."
    return None


def classification(y: np.ndarray, flagged: np.ndarray) -> dict:
    y = y.astype(int)
    flagged = flagged.astype(bool)
    tp = int(((y == 1) & flagged).sum()); fp = int(((y == 0) & flagged).sum())
    fn = int(((y == 1) & ~flagged).sum()); tn = int(((y == 0) & ~flagged).sum())
    precision = tp / (tp + fp) if tp + fp else 0.0
    recall = tp / (tp + fn) if tp + fn else 0.0
    return {
        "flagged": tp + fp, "true_positives": tp, "false_positives": fp, "false_negatives": fn, "true_negatives": tn,
        "precision": round(precision, 4), "recall": round(recall, 4),
        "f1": round(2 * precision * recall / (precision + recall), 4) if precision + recall else 0.0,
        "false_positive_rate": round(fp / (fp + tn), 4) if fp + tn else 0.0,
    }


def evaluate(frame: pd.DataFrame, label_column: str, test_months: int, flag_rate: float,
             active: Bundle | None = None) -> dict:
    """Train on earlier months, test on the most recent ones: the model is judged on months it never saw."""
    periods = sorted(frame["period"].unique())
    test_periods = set(periods[-test_months:])
    train = frame[~frame["period"].isin(test_periods)]
    test = frame[frame["period"].isin(test_periods) & frame[label_column].notna()]
    if train.empty or test.empty or test[label_column].nunique() < 2:
        return {"evaluated": False, "reason": "Not enough labelled data in the test window for both classes."}

    y = test[label_column].to_numpy().astype(int)
    candidate = fit(train, flag_rate)
    cand_scores = candidate.score(test)
    cand_flags = cand_scores >= candidate.threshold
    # Fair comparison: rank by distance from the contracted fee and flag the same number of rows.
    ratio_distance = np.abs(test["log_ratio_contract"].to_numpy())
    budget = int(cand_flags.sum())
    ranked = np.zeros(len(test), dtype=bool)
    if budget:
        ranked[np.argsort(-ratio_distance)[:budget]] = True

    result = {
        "evaluated": True,
        "label_column": label_column,
        "train_months": len(periods) - len(test_periods),
        "test_months": len(test_periods),
        "test_rows": int(len(test)),
        "test_positives": int(y.sum()),
        "candidate": {**classification(y, cand_flags), "average_precision": round(float(average_precision_score(y, cand_scores)), 4)},
        "rule": classification(y, rule_flags(test)),
        "ratio_ranked_same_budget": {**classification(y, ranked),
                                     "average_precision": round(float(average_precision_score(y, ratio_distance)), 4)},
    }
    if active is not None:
        try:
            active_scores = active.score(test)
            result["active"] = {**classification(y, active_scores >= active.threshold),
                                "average_precision": round(float(average_precision_score(y, active_scores)), 4)}
        except KeyError:
            result["active"] = {"note": "The active model uses a different feature set and was not compared."}
    if "syntheticScenario" in test.columns:
        by_scenario = {}
        for scenario, group in test[test["syntheticScenario"].notna()].groupby("syntheticScenario"):
            idx = test.index.get_indexer(group.index)
            by_scenario[scenario] = {
                "rows": int(len(group)),
                "caught_by_candidate": int(cand_flags[idx].sum()),
                "caught_by_rule": int(rule_flags(group).sum()),
            }
        result["recall_by_scenario"] = by_scenario
    return result
