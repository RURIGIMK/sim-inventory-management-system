# SIM Cost Anomaly Model Artifacts

This directory stores the exported model and its metadata from the synthetic SIM cost anomaly experiment.

Expected files:

- `sim_cost_isolation_forest.joblib`: trained scikit-learn Isolation Forest model.
- `sim_cost_model_metadata.json`: model configuration and feature metadata.

## Important limitation

The model was trained and evaluated using synthetic demonstration records only. Its reported evaluation results do not establish performance on real organisational SIM data. Do not use this model to make operational decisions or automatically suspend SIMs.

The model expects three numeric features in this order:

1. `monthly_cost`
2. `expected_cost`
3. `cost_ratio`

The expected-cost baseline must be appropriate to the data being evaluated. The notebook's synthetic plan prices must not be treated as actual organisational prices.
