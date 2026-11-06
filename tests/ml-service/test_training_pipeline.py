from __future__ import annotations

import importlib.util
from argparse import Namespace
from pathlib import Path

import joblib
import pandas as pd
import pytest

SCRIPT = Path(__file__).resolve().parents[2] / "notebooks" / "sim_cost_model.py"
SPEC = importlib.util.spec_from_file_location("sim_cost_model", SCRIPT)
pipeline = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
SPEC.loader.exec_module(pipeline)


def args(tmp_path: Path, **overrides) -> Namespace:
    values = {
        "input_csv": None,
        "model_dir": str(tmp_path / "models"),
        "results_dir": str(tmp_path / "results"),
        "records": 240,
        "anomaly_rate": 0.08,
        "seed": 17,
        "test_size": 0.30,
        "n_estimators": 80,
        "contamination": 0.08,
    }
    values.update(overrides)
    return Namespace(**values)


def test_generator_is_reproducible_and_uses_requested_record_count():
    first = pipeline.generate_synthetic_data(240, 0.08, 17)
    second = pipeline.generate_synthetic_data(240, 0.08, 17)

    pd.testing.assert_frame_equal(first, second)
    assert len(first) == 240
    assert first[pipeline.LABEL_COLUMN].sum() == 19


def test_pipeline_exports_model_metadata_and_held_out_evaluation(tmp_path):
    result = pipeline.run_experiment(args(tmp_path))

    model_path = Path(result["model_path"])
    metadata_path = Path(result["metadata_path"])
    evaluation_path = Path(result["evaluation_path"])
    assert model_path.is_file()
    assert metadata_path.is_file()
    assert evaluation_path.is_file()

    metadata = result["metadata"]
    assert metadata["dataset_records"] == 240
    assert metadata["training_records"] + metadata["test_records"] == 240
    assert metadata["training_data_source"] == "synthetic"
    assert metadata["evaluation_metrics"]["baseline"]["records_evaluated"] == 72
    assert metadata["evaluation_metrics"]["isolation_forest"]["records_evaluated"] == 72

    model = joblib.load(model_path)
    evaluation = pd.read_csv(evaluation_path)
    features = evaluation[pipeline.FEATURES]
    assert len(model.predict(features)) == len(evaluation)


def test_unlabelled_csv_trains_without_claiming_classification_metrics(tmp_path):
    source = pipeline.generate_synthetic_data(120, 0.05, 23).drop(columns=[pipeline.LABEL_COLUMN])
    csv_path = tmp_path / "approved.csv"
    source.to_csv(csv_path, index=False)

    result = pipeline.run_experiment(args(tmp_path, input_csv=str(csv_path)))
    assert result["metadata"]["training_data_source"] == "approved_csv"
    assert result["metadata"]["evaluation_metrics"] is None
    assert result["metadata"]["test_records"] == 0


def test_invalid_costs_are_rejected(tmp_path):
    source = pipeline.generate_synthetic_data(120, 0.05, 23)
    source.loc[0, "expected_cost"] = 0
    csv_path = tmp_path / "invalid.csv"
    source.to_csv(csv_path, index=False)

    with pytest.raises(ValueError, match="expected_cost must be > 0"):
        pipeline.load_dataset(csv_path, args(tmp_path))


def test_labelled_dataset_requires_both_classes_with_enough_rows(tmp_path):
    source = pipeline.generate_synthetic_data(120, 0.05, 23)
    source[pipeline.LABEL_COLUMN] = 0
    source.loc[0, pipeline.LABEL_COLUMN] = 1
    csv_path = tmp_path / "one-anomaly.csv"
    source.to_csv(csv_path, index=False)

    with pytest.raises(ValueError, match="At least two records per class"):
        pipeline.run_experiment(args(tmp_path, input_csv=str(csv_path)))
