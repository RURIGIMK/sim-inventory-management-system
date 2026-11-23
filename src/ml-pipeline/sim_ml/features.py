"""Turns the API's raw billing history into one feature row per SIM per month.

Every feature only uses information available at the end of that month (the SIM's own earlier
months, the same month's peers and the custody record), so a score never depends on the future.
Synthetic scenario labels and human reviews are carried as label columns and are never features.
"""
from __future__ import annotations

import numpy as np
import pandas as pd

HISTORY_MONTHS = 6

# Features the model sees. Ratios are log-transformed so "twice" and "half" are equally far from normal.
MODEL_FEATURES = [
    "log_ratio_contract",
    "log_ratio_own_history",
    "log_ratio_previous_month",
    "log_ratio_peers",
    "log_data_vs_history",
    "log_voice_vs_history",
    "zero_usage",
    "zero_usage_streak",
    "holder_inactive",
]


def _safe_log_ratio(numerator: pd.Series, denominator: pd.Series) -> pd.Series:
    """log(a/b) with a small floor so zero usage or zero bills stay finite; 0 when there is no baseline."""
    num = numerator.astype(float)
    den = denominator.astype(float)
    out = np.log((num + 1.0) / (den + 1.0))
    return out.where(den.notna(), 0.0).fillna(0.0)


def build_frame(dataset: dict) -> pd.DataFrame:
    sims = pd.DataFrame(dataset.get("sims", []))
    costs = pd.DataFrame(dataset.get("costs", []))
    if costs.empty or sims.empty:
        return pd.DataFrame(columns=["simId", "period", *MODEL_FEATURES])

    sims = sims.rename(columns={"id": "simId"})
    frame = costs.merge(sims[["simId", "operator", "planType", "contractedMonthlyCost"]], on="simId", how="inner")
    frame["period"] = pd.to_datetime(frame["period"] + "-01")
    frame["amount"] = frame["amount"].astype(float)
    frame["contracted"] = frame["contractedMonthlyCost"].astype(float)
    frame["dataUsageMb"] = pd.to_numeric(frame.get("dataUsageMb"), errors="coerce")
    frame["voiceMinutes"] = pd.to_numeric(frame.get("voiceMinutes"), errors="coerce")
    frame = frame.sort_values(["simId", "period"]).reset_index(drop=True)

    by_sim = frame.groupby("simId", group_keys=False)

    def trailing_median(column: str) -> pd.Series:
        return by_sim[column].apply(lambda s: s.shift(1).rolling(HISTORY_MONTHS, min_periods=2).median())

    frame["history_amount"] = trailing_median("amount")
    frame["history_data"] = trailing_median("dataUsageMb")
    frame["history_voice"] = trailing_median("voiceMinutes")
    frame["previous_amount"] = by_sim["amount"].shift(1)

    # How this SIM's bill compares with similar SIMs (same operator and plan) in the same month,
    # each normalised by its own contracted fee and excluding the SIM itself.
    frame["ratio_contract"] = (frame["amount"] + 1.0) / (frame["contracted"] + 1.0)
    peer_key = ["period", "operator", "planType"]
    peer_sum = frame.groupby(peer_key)["ratio_contract"].transform("sum")
    peer_count = frame.groupby(peer_key)["ratio_contract"].transform("count")
    frame["peer_ratio_contract"] = (peer_sum - frame["ratio_contract"]) / (peer_count - 1).replace(0, np.nan)

    frame["log_ratio_contract"] = _safe_log_ratio(frame["amount"], frame["contracted"])
    frame["log_ratio_own_history"] = _safe_log_ratio(frame["amount"], frame["history_amount"])
    frame["log_ratio_previous_month"] = _safe_log_ratio(frame["amount"], frame["previous_amount"])
    frame["log_ratio_peers"] = (np.log(frame["ratio_contract"])
                                - np.log(frame["peer_ratio_contract"].fillna(frame["ratio_contract"]).clip(lower=1e-3)))
    frame["log_data_vs_history"] = _safe_log_ratio(frame["dataUsageMb"], frame["history_data"])
    frame["log_voice_vs_history"] = _safe_log_ratio(frame["voiceMinutes"], frame["history_voice"])

    usage_known = frame["dataUsageMb"].notna() & frame["voiceMinutes"].notna()
    frame["zero_usage"] = (usage_known & (frame["dataUsageMb"] == 0) & (frame["voiceMinutes"] == 0)).astype(int)
    frame["zero_usage_streak"] = by_sim["zero_usage"].apply(
        lambda s: s.groupby((s != s.shift()).cumsum()).cumsum() * s)

    frame["holder_inactive"] = _holder_inactive(frame, dataset)
    frame["label_review"] = _review_labels(frame, dataset)
    scenario = frame.get("syntheticScenario")
    frame["label_synthetic"] = np.where(
        frame["source"] == "synthetic", scenario.notna().astype(float) if scenario is not None else 0.0, np.nan)
    frame["periodText"] = frame["period"].dt.strftime("%Y-%m")
    return frame


def _holder_inactive(frame: pd.DataFrame, dataset: dict) -> pd.Series:
    """1 when, at the end of the month, the SIM is held by someone the HR feed marks as not active."""
    assignments = pd.DataFrame(dataset.get("assignments", []))
    employees = pd.DataFrame(dataset.get("employees", []))
    if assignments.empty or employees.empty:
        return pd.Series(0, index=frame.index)
    inactive = set(employees.loc[employees["status"] != "Active", "id"])
    assignments["assignedAt"] = pd.to_datetime(assignments["assignedAt"], utc=True).dt.tz_localize(None)
    assignments["returnedAt"] = pd.to_datetime(assignments["returnedAt"], utc=True).dt.tz_localize(None)
    month_end = frame["period"] + pd.offsets.MonthEnd(0)
    result = np.zeros(len(frame), dtype=int)
    for _, a in assignments[assignments["employeeId"].isin(inactive)].iterrows():
        held = (frame["simId"] == a["simId"]) & (a["assignedAt"] <= month_end)
        if not pd.isna(a["returnedAt"]):
            held &= month_end < a["returnedAt"]
        result |= held.to_numpy().astype(int)
    return pd.Series(result, index=frame.index)


def _review_labels(frame: pd.DataFrame, dataset: dict) -> pd.Series:
    reviews = pd.DataFrame(dataset.get("reviews", []))
    if reviews.empty:
        return pd.Series(np.nan, index=frame.index)
    reviews["period"] = pd.to_datetime(reviews["period"] + "-01")
    reviews["label"] = (reviews["decision"] == "confirmed").astype(float)
    merged = frame[["simId", "period"]].merge(reviews[["simId", "period", "label"]], on=["simId", "period"], how="left")
    return pd.Series(merged["label"].to_numpy(), index=frame.index)
