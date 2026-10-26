# Optional SIM Asset Intelligence Prototype

Machine learning is a separate proof of concept, not required for the core inventory system. Do not use real employee or operator data in Colab unless explicit authorization and an approved data-handling plan exist.

## Proposed experiments

1. **Cost anomaly flags:** use synthetic monthly costs grouped by plan type, operator and cost centre. Compare robust statistical thresholds with Isolation Forest.
2. **Contract/asset review queue:** use deterministic rules to flag expired contracts, missing ownership and unusually long unassigned periods. This may be more explainable than ML.
3. **Idle SIM candidates:** only investigate if an authorized, lawful source of usage data is made available. Inventory status alone cannot prove that a SIM is unused.
4. **Cost forecasting:** only after sufficient time-series data exists; compare a simple seasonal-naive baseline before trying more complex models.

## Colab workflow

- Generate synthetic data with documented distributions and a fixed random seed.
- Split data by time or held-out records to avoid leakage.
- Compare every model with a transparent baseline.
- Record precision, recall, false-positive rate and operational usefulness.
- Export model code, feature definitions and a small synthetic test fixture. Never export credentials or personal data in a notebook.
- Treat predictions as review suggestions only. A human must verify each flag. Do not automatically suspend a SIM, penalize an employee or change a role based on a model score.

## Integration decision

Do not add a model-serving service until a baseline experiment demonstrates measurable value. If justified, run scoring as a scheduled Python job or isolated service that reads minimized, authorized data and writes explainable recommendations. The .NET API remains the system of record.
