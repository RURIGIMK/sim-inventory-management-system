# Development Dataset

## Purpose

The project is an organizational SIM inventory management system, not a machine-learning application. The data in this directory is therefore synthetic development data, not a training dataset.

It exists to support:

- UI development
- API development
- database seeding
- functional testing
- reporting demonstrations
- audit-log demonstrations

## Data Fields

sample_sims.csv contains representative SIM inventory records.

| Field | Description |
| --- | --- |
| sim_id | Internal synthetic SIM identifier |
| operator | Mobile network operator |
| phone_number | Synthetic demonstration phone number |
| iccid | Synthetic ICCID-like identifier |
| plan_type | Example service plan |
| monthly_cost | Example monthly cost |
| contract_start | Example contract start date |
| contract_end | Example contract end date |
| status | Inventory status |
| cost_centre | Example organizational cost centre |

## Important

All values are synthetic. Do not replace these records with real employee or SIM information in a public repository.

If a real organizational dataset is used during evaluation, it should be anonymized and handled according to the applicable institutional and data-protection requirements.
