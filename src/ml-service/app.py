"""Inference API for the synthetic SIM cost anomaly model.

This service is intentionally independent of the application's database.
Only synthetic or explicitly approved records should be sent to it.
"""

import json
import os
from pathlib import Path
from typing import Any

import joblib
import pandas as pd
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

MODEL_PATH = Path(
    os.getenv(
        "MODEL_PATH",
        Path(__file__).resolve().parents[2]
        / "notebooks"
        / "models"
        / "sim_cost_isolation_forest.joblib",
    )
)

app = FastAPI(
    title="SIM Cost Anomaly Inference API",
    version="0.1.0",
    description="Prototype inference API for the synthetic SIM cost anomaly model.",
)

model: Any | None = None
model_metadata: dict[str, Any] = {}
model_load_error: str | None = None

try:
    if MODEL_PATH.is_file():
        model = joblib.load(MODEL_PATH)
        metadata_path = Path(
            os.getenv("MODEL_METADATA_PATH", str(MODEL_PATH.with_name("sim_cost_model_metadata.json")))
        )
        if metadata_path.is_file():
            model_metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        if hasattr(model, "feature_names_in_"):
            expected_features = ["monthly_cost", "expected_cost", "cost_ratio"]
            if list(model.feature_names_in_) != expected_features:
                raise ValueError("Model feature schema does not match the inference API.")
    else:
        model_load_error = f"Model file not found: {MODEL_PATH}"
except Exception as exc:  # Keep service startup available for health diagnostics.
    model_load_error = str(exc)


class PredictionRequest(BaseModel):
    monthly_cost: float = Field(ge=0, description="Monthly SIM cost")
    expected_cost: float = Field(gt=0, description="Approved expected monthly cost baseline")


class PredictionResponse(BaseModel):
    is_anomaly: bool
    prediction: int
    decision_score: float
    cost_ratio: float
    model_version: str
    data_classification: str


@app.get("/health")
def health() -> dict[str, str | bool]:
    return {
        "status": "ok" if model is not None else "model_unavailable",
        "model_loaded": model is not None,
        "model_error": model_load_error or "",
        "model_version": str(model_metadata.get("model_version", "unknown")),
    }


@app.post("/predict", response_model=PredictionResponse)
def predict(request: PredictionRequest) -> PredictionResponse:
    if model is None:
        raise HTTPException(
            status_code=503,
            detail="Model is not loaded. Check MODEL_PATH and the model artifact.",
        )

    cost_ratio = request.monthly_cost / request.expected_cost
    features = pd.DataFrame(
        [[request.monthly_cost, request.expected_cost, cost_ratio]],
        columns=["monthly_cost", "expected_cost", "cost_ratio"],
    )

    try:
        prediction = int(model.predict(features)[0])
        decision_score = float(model.decision_function(features)[0])
    except Exception as exc:
        raise HTTPException(status_code=500, detail="Model prediction failed.") from exc

    return PredictionResponse(
        is_anomaly=prediction == -1,
        prediction=prediction,
        decision_score=decision_score,
        cost_ratio=cost_ratio,
        model_version=str(model_metadata.get("model_version", "unknown")),
        data_classification=str(model_metadata.get("data_classification", "Unclassified model artifact")),
    )
