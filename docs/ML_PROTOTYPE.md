# SIM Asset Intelligence ML Experiment

The ML work is an experimental decision-support component. It is not required for core inventory operations and is not currently integrated with the live application database or operational Reports page.

## Reproducible training workflow

The training logic is maintained in `notebooks/sim_cost_model.py`. The Colab notebook `notebooks/sim_cost_anomaly_demo.ipynb` clones the repository and runs that same script, so local and Colab execution share one implementation. It installs the ML development requirements, verifies the newly exported artifact by loading it through the FastAPI inference module and making a local prediction, and runs the ML pytest suite.

Run from the repository root:

```bash
python -m pip install -r src/ml-service/requirements.txt
python notebooks/sim_cost_model.py
```

Optional parameters:

```bash
python notebooks/sim_cost_model.py \
  --records 800 \
  --anomaly-rate 0.06 \
  --seed 42 \
  --test-size 0.30 \
  --n-estimators 250 \
  --contamination 0.05 \
  --model-dir notebooks/models \
  --results-dir notebooks/results
```

By default, the pipeline generates synthetic records. It supports an optional CSV with required numeric columns `monthly_cost` and `expected_cost`. An optional `known_synthetic_anomaly` column (0 = normal, 1 = anomaly) enables held-out labelled evaluation. An unlabelled CSV can be scored, but the pipeline intentionally does not report precision, recall, F1 or false-positive rate without ground-truth labels.

Only upload approved, de-identified data. The CSV must not contain employee names, phone numbers, ICCIDs, invoices, credentials or unrelated personal information. The expected-cost field must be a justified baseline, not an assumed invoice amount.

## Outputs and traceability

The training script writes:
- `notebooks/models/sim_cost_isolation_forest.joblib`: fitted Isolation Forest artifact.
- `notebooks/models/sim_cost_model_metadata.json`: model version, feature order, configuration, data classification, training/test counts, dataset fingerprint, library version and evaluation metrics when labels exist.
- `notebooks/results/sim_anomaly_evaluation.csv`: held-out records, predictions, anomaly scores and baseline comparison when labels exist.
- `notebooks/results/run_summary.json`: run output locations and metadata.

Keep the model artifact and metadata together. The FastAPI service reads the model version and data classification from the adjacent metadata file. Do not replace a deployed model artifact without reviewing its metadata and evaluation.

## Evaluation methodology and limitations

The pipeline uses a fixed random seed for repeatable synthetic generation and a stratified train/test split when labels exist. Isolation Forest is fitted on training features only. It is compared with a transparent cost-ratio rule (ratio above 1.8 or below 0.35).

Synthetic anomaly multipliers intentionally overlap part of the normal range, so the reference rule is not guaranteed to recover every injected label. This is still a controlled demonstration, not a substitute for representative, independently labelled billing data. Review the confusion matrix, precision, recall, F1 and false-positive rate rather than relying on a single accuracy figure.

For unlabelled datasets, the model can produce anomaly scores and flags, but no classification-performance claim is valid. A flag is an investigation prompt, not proof of overbilling or misconduct.

A successful GitHub Actions run verifies automated checks only. Complete the notebook from first to last cell in a fresh Colab runtime and inspect the actual metrics and inference output before claiming the experiment is reproducible end-to-end.

## Current application boundary

- The application Intelligence page uses deterministic, explainable rules in the .NET API. It does not use Isolation Forest.
- The FastAPI inference prototype in `src/ml-service` is separate and is not deployed or called by the .NET API.
- The Reports page shows current operational aggregates and CSV export. It does not currently show ML predictions or model evaluation metrics.
- The SIM schema does not include invoice history, usage time series or a verified expected-cost field.

Do not connect the service to PostgreSQL or expose it directly to browsers. If later justified by evaluation, .NET should call it server-to-server with authentication, timeouts and safe unavailable-service behaviour. ML output must remain human-reviewed decision support.

## Before organizational use

Obtain written authorization, minimize exported fields, remove direct identifiers, define an approved expected-cost baseline and obtain meaningful review labels. Evaluate false positives and missed anomalies with domain reviewers. Establish model versioning, monitoring, rollback and retention controls. Never automatically suspend SIMs or take employee/account action from a prediction.
