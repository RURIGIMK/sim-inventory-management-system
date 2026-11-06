# SIM Cost Anomaly Inference Service

A small Python API for loading the exported Isolation Forest model and scoring cost records.

## Model artifact

The exported model is stored at `notebooks/models/sim_cost_isolation_forest.joblib`. The service loads this file by default. Set `MODEL_PATH` to override the location. The adjacent `sim_cost_model_metadata.json` supplies the model version and data classification; set `MODEL_METADATA_PATH` to override its location.

The dependency file pins scikit-learn to version `1.6.1`, matching the version recorded in the model metadata.

## Train or refresh the model

From the repository root, install the ML dependencies and run the reproducible training pipeline:

```bash
python -m pip install -r src/ml-service/requirements.txt
python notebooks/sim_cost_model.py
```

The pipeline writes the model and metadata under `notebooks/models/` and evaluation outputs under `notebooks/results/`. It generates synthetic data by default. Read [the ML experiment documentation](../../docs/ML_PROTOTYPE.md) before supplying any CSV.

## Run locally

From the repository root:

```bash
python -m venv .venv
# Windows:
.venv\\Scripts\\activate
# Linux/macOS:
source .venv/bin/activate

pip install -r src/ml-service/requirements.txt
uvicorn app:app --app-dir src/ml-service --host 127.0.0.1 --port 8001
```

Set `MODEL_PATH` if the model is stored elsewhere.

## Endpoint

- `GET /health`: reports whether the model loaded.
- `POST /predict`: accepts `monthly_cost` and `expected_cost`; calculates `cost_ratio` and returns the Isolation Forest result and decision score.

Example request:

```json
{
  "monthly_cost": 1500,
  "expected_cost": 1400
}
```

## Safety and integration notes

- Send only synthetic or explicitly approved data during development.
- Do not connect this service directly to PostgreSQL.
- Keep it behind the authenticated ASP.NET API; do not expose it directly to browsers.
- The current model is synthetic-data-trained and is for demonstration only.
- A valid, organisation-approved expected-cost baseline is required before scoring organisational records.
- A model anomaly flag is an investigation signal, not proof of an incorrect charge.
- This service does not modify SIM status or take operational action.

## Automated tests

From the repository root, activate your Python virtual environment and install the test dependencies:

```bash
pip install -r src/ml-service/requirements-dev.txt
```

Run the service tests:

```bash
python -m pytest tests/ml-service -v
```

The tests cover health reporting, model metadata reporting, normal and anomalous response handling, request validation, unavailable-model handling, and the reproducible training pipeline including model serialization. Stub-model API tests do not measure predictive accuracy. The training-pipeline tests verify artifact round-tripping and metadata generation, not real-world accuracy.
