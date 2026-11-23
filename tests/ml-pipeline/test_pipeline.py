import copy
import json

import numpy as np
import pytest

from conftest import make_dataset
from sim_ml import model as m
from sim_ml.client import ApiClient
from sim_ml.features import MODEL_FEATURES, build_frame
from sim_ml.run import choose_labels, main, score_rows


def test_features_never_use_labels(dataset):
    assert not any("label" in f or "scenario" in f.lower() for f in MODEL_FEATURES)


def test_features_do_not_look_into_the_future(dataset):
    base = build_frame(dataset)
    changed = copy.deepcopy(dataset)
    last = max(c["period"] for c in changed["costs"])
    for c in changed["costs"]:
        if c["period"] == last:
            c["amount"] *= 10
    after = build_frame(changed)
    earlier = base["period"] < base["period"].max()
    np.testing.assert_allclose(base.loc[earlier, MODEL_FEATURES].to_numpy(), after.loc[earlier, MODEL_FEATURES].to_numpy())


def test_zero_usage_streak_counts_consecutive_months(dataset):
    frame = build_frame(dataset)
    dormant = frame[frame["syntheticScenario"] == "dormant_billed"].sort_values(["simId", "period"])
    first_sim = dormant[dormant["simId"] == dormant["simId"].iloc[0]]
    assert first_sim["zero_usage_streak"].tolist() == [1, 2, 3, 4]


def test_bundle_round_trip_is_safe_and_identical(dataset):
    frame = build_frame(dataset)
    bundle = m.fit(frame, 0.05)
    restored = m.Bundle.from_bytes(bundle.to_bytes())
    np.testing.assert_allclose(bundle.score(frame), restored.score(frame))
    assert restored.threshold == pytest.approx(bundle.threshold)


class NotAModel:
    """Stands in for an object an attacker could plant in a tampered artifact."""


def test_untrusted_artifact_types_are_refused():
    import skops.io as sio
    payload = sio.dumps({"model": NotAModel(), "features": [], "threshold": 0.0,
                         "median": np.zeros(1), "scale": np.ones(1)})
    with pytest.raises(RuntimeError, match="unexpected types"):
        m.Bundle.from_bytes(payload)


def test_evaluation_compares_candidate_with_rule_on_unseen_months(dataset):
    frame = build_frame(dataset)
    result = m.evaluate(frame, "label_synthetic", test_months=5, flag_rate=0.05)
    assert result["evaluated"] and result["test_months"] == 5
    assert {"candidate", "rule", "ratio_ranked_same_budget"} <= result.keys()
    dormant = result["recall_by_scenario"]["dormant_billed"]
    assert dormant["caught_by_rule"] == 0          # the rule cannot see a bill that matches the contract
    assert dormant["caught_by_candidate"] == dormant["rows"]


def test_reviews_replace_synthetic_labels_once_there_are_enough(dataset):
    frame = build_frame(dataset)
    assert choose_labels(frame, min_reviews=30) == "label_synthetic"
    reviewed = copy.deepcopy(dataset)
    reviewed["reviews"] = [{"simId": c["simId"], "period": c["period"],
                            "decision": "confirmed" if c["syntheticScenario"] else "dismissed"}
                           for c in reviewed["costs"][:40]] + [
                           {"simId": c["simId"], "period": c["period"], "decision": "confirmed"}
                           for c in reviewed["costs"] if c["syntheticScenario"]]
    assert choose_labels(build_frame(reviewed), min_reviews=30) == "label_review"


def test_flagged_scores_carry_plain_language_reasons(dataset):
    frame = build_frame(dataset)
    bundle = m.fit(frame, 0.05)
    scores = score_rows(bundle, frame)
    flagged = [s for s in scores if s["isFlagged"]]
    assert flagged and all(s["reasons"] for s in flagged)
    assert all(not s["reasons"] for s in scores if not s["isFlagged"])
    assert {"simId", "period", "score", "isFlagged", "reasons"} <= scores[0].keys()


def test_offline_dry_run_and_short_history_skip(tmp_path, capsys):
    full = tmp_path / "full.json"
    full.write_text(json.dumps(make_dataset()))
    assert main(["--dataset-json", str(full), "--dry-run", "--summary-json", str(tmp_path / "s.json")]) == 0
    summary = json.loads((tmp_path / "s.json").read_text())
    assert summary["evaluation"]["evaluated"] and "dry run" in summary["status"]

    short = tmp_path / "short.json"
    short.write_text(json.dumps(make_dataset(n_sims=5, months=3)))
    main(["--dataset-json", str(short), "--dry-run", "--summary-json", str(tmp_path / "t.json")])
    assert json.loads((tmp_path / "t.json").read_text())["status"].startswith("skipped")


def test_client_rejects_plain_http_and_weak_tokens():
    with pytest.raises(ValueError):
        ApiClient("http://api.example.com", "x" * 40)
    with pytest.raises(ValueError):
        ApiClient("https://api.example.com", "short")
