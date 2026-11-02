from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
import pytest
from fastapi.testclient import TestClient

SERVICE_DIR = Path(__file__).resolve().parents[2] / "src" / "ml-service"
sys.path.insert(0, str(SERVICE_DIR))

import app as ml_app  # noqa: E402


class StubModel:
    """Small deterministic stand-in for the exported Isolation Forest."""

    def predict(self, features):
        return np.where(features["cost_ratio"].to_numpy() > 1.8, -1, 1)

    def decision_function(self, features):
        return np.where(features["cost_ratio"].to_numpy() > 1.8, -0.2, 0.2)


@pytest.fixture
def client():
    original_model = ml_app.model
    original_error = ml_app.model_load_error
    ml_app.model = StubModel()
    ml_app.model_load_error = None

    with TestClient(ml_app.app) as test_client:
        yield test_client

    ml_app.model = original_model
    ml_app.model_load_error = original_error


def test_health_reports_loaded_model(client):
    response = client.get("/health")

    assert response.status_code == 200
    assert response.json()["status"] == "ok"
    assert response.json()["model_loaded"] is True
    assert response.json()["model_error"] == ""


def test_normal_cost_returns_inlier_prediction(client):
    response = client.post(
        "/predict",
        json={"monthly_cost": 1500, "expected_cost": 1400},
    )

    assert response.status_code == 200
    body = response.json()
    assert body["is_anomaly"] is False
    assert body["prediction"] == 1
    assert body["cost_ratio"] == pytest.approx(1500 / 1400)
    assert body["model_version"] == "1.0.0"


def test_high_cost_ratio_returns_anomaly_prediction(client):
    response = client.post(
        "/predict",
        json={"monthly_cost": 2000, "expected_cost": 1000},
    )

    assert response.status_code == 200
    body = response.json()
    assert body["is_anomaly"] is True
    assert body["prediction"] == -1
    assert body["cost_ratio"] == pytest.approx(2.0)


@pytest.mark.parametrize("monthly_cost", [-1, -0.01])
def test_negative_monthly_cost_is_rejected(client, monthly_cost):
    response = client.post(
        "/predict",
        json={"monthly_cost": monthly_cost, "expected_cost": 1000},
    )

    assert response.status_code == 422


@pytest.mark.parametrize("expected_cost", [0, -1, -0.01])
def test_non_positive_expected_cost_is_rejected(client, expected_cost):
    response = client.post(
        "/predict",
        json={"monthly_cost": 1000, "expected_cost": expected_cost},
    )

    assert response.status_code == 422


def test_missing_model_returns_service_unavailable(client):
    ml_app.model = None
    ml_app.model_load_error = "Test model unavailable"

    response = client.post(
        "/predict",
        json={"monthly_cost": 1000, "expected_cost": 1000},
    )

    assert response.status_code == 503
    assert response.json()["detail"] == (
        "Model is not loaded. Check MODEL_PATH and the model artifact."
    )
