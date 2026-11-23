# Billing history and the anomaly model loop

This replaces the isolated Colab experiment with a model that is connected to the running system and improves as
billing data and human reviews accumulate, without ever acting on its own.

```
operator invoice CSV ──> SimCostRecords (per SIM, per month)
                                │
            nightly ml-pipeline workflow (or the Colab notebook)
            GET /api/integrations/ml/dataset   (no names, numbers or ICCIDs)
            features → time-ordered evaluation → candidate model
            POST /api/integrations/ml/models   (registered as "candidate")
            POST /api/integrations/ml/scores   (candidate and active model)
                                │
            Intelligence page: rule flag and model flag side by side
            administrator activates a candidate (or rolls back)  ── audited
            reviewers confirm or dismiss flags                    ── audited
                                │
            reviews become the labels the next candidate is judged on
```

## Data

* `Sims.MonthlyCost` is the **contracted** monthly fee: the expected cost.
* `SimCostRecords` holds what was **billed** for each SIM and month, from operator invoices
  (`/api/admin/billing/import`, all-or-nothing, never silently overwritten).
* Demonstration data: `seed-demo` generates ~17 months of synthetic invoices with labelled scenarios
  (roaming spike, plan too small for usage, billed while unused, charged twice). The label is stored for evaluation
  only, a database constraint keeps it off real invoice rows, and it is never a model feature or shown to reviewers.

## Features (`src/ml-pipeline/sim_ml/features.py`)

Each SIM-month is described only with information available at the end of that month: bill vs contract, bill vs the
SIM's own previous six months, bill vs the previous month, bill vs same-operator/same-plan peers, data and voice usage
vs the SIM's history, consecutive months with no usage, and whether the current holder is no longer active in HR.
A test changes the last month's bills and asserts that no earlier month's features move.

## Model and evaluation (`sim_ml/model.py`)

* Isolation Forest (300 trees) over the nine features; the flag threshold is the 95th percentile of training scores.
* Evaluation is **time-ordered**: train on earlier months, test on the five most recent months.
* Compared on the same test months with: the rule (bill ≥1.5× or ≤0.5× contract, identical to the API), ranking by
  distance from the contract with the same number of flags, and the currently active model.
* Labels: human reviews once at least 30 exist with both outcomes; synthetic scenarios until then; none for real
  invoices without reviews (no metrics are invented).
* Each flag carries plain-language reasons (e.g. "No data or voice usage for 3 months while still being billed").
* Artifacts are serialised with `skops`, which refuses to load unexpected types, so a tampered artifact in the database
  cannot execute code in the pipeline. Checksums are verified on download.

Synthetic result at the time of writing (510 SIM-months, last 5 months held out, 11 known anomalies): the candidate
flagged 18 with precision 0.61 and recall 1.00; the rule flagged 6 with precision 1.00 and recall 0.55. The rule cannot
see a SIM that is billed exactly its contract while unused (0 of 4); the model caught all 4. On synthetic data this is a
statement about the generator, not about real billing.

## Governance

* The pipeline only **registers candidates**. Activation and rollback are administrator actions in the Intelligence page,
  recorded as `ml_model_activated`.
* At most one model is active (database partial unique index). Older versions keep their artifacts for rollback.
* Scores and reviews never change a SIM, an assignment or an account.

## Running it

* Nightly: `.github/workflows/ml-pipeline.yml` (enable with repository variable `ML_PIPELINE_ENABLED=true`;
  secret `SIM_INTEGRATION_TOKEN`; variable `RENDER_API_BASE_URL`). Manual runs can be dry runs.
* Colab: `notebooks/sim_cost_pipeline_live.ipynb`, offline with synthetic data or live against the API.
* Locally: `cd src/ml-pipeline && SIM_INTEGRATION_TOKEN=... python -m sim_ml.run --api-base https://... --dry-run`.

The earlier `notebooks/sim_cost_model.py` experiment and `src/ml-service` FastAPI prototype are kept for the record of
how the work evolved; the connected pipeline above supersedes them.
