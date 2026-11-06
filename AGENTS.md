# Agent Instructions and Project Handover

Last verified: 2026-11-06
Repository: `RURIGIMK/sim-inventory-management-system`
Default branch: `main`
Verified main commit after PR #13: `2a48ee7fd6f13b1368b97638931042de275ff0df`
Latest completed changes: PR #11 (agent handover), PR #12 (reproducible ML training/evaluation) and PR #13 (fresh Colab validation preparation) are merged.

Read this file before changing code. It is the continuity source for future developers and AI agents. Keep it current when implementation status, decisions or next steps change. Verify repository state rather than trusting an old chat summary.

## 1. Purpose and scope

Academic prototype for organizational SIM asset tracking, employee assignment, lifecycle management, reporting and audit visibility. It is not approved for production use with real employee, phone, ICCID, invoice, usage or billing data. Use synthetic data only unless written authorization and a data-handling plan exist.

Prioritize business correctness, traceability, explainable outputs, usable reporting and maintainable low-cost hosting. Avoid unnecessary services and complexity.

## 2. Verified architecture

| Layer | Implementation |
| --- | --- |
| Frontend | Next.js 15.5.28, React 19, TypeScript, App Router, static export |
| API | ASP.NET Core 8 minimal API |
| Persistence | EF Core 8 / Npgsql; Neon PostgreSQL hosted, Docker PostgreSQL locally |
| Identity | Google ID-token validation and administrator-provisioned email allowlist |
| Authorization | API-enforced Administrator, Departmental Manager and Viewer roles |
| API hosting | Render |
| Frontend hosting | Cloudflare Pages |
| CI/CD | GitHub Actions; deploy gated by `DEPLOY_ENABLED` |
| Deterministic intelligence | .NET rules in `src/backend/SimInventory.Api/Intelligence/SimAssetIntelligence.cs` |
| ML experiment | Google Colab notebook, synthetic data and exported Isolation Forest artifact |
| ML inference prototype | Separate FastAPI service in `src/ml-service`; not integrated with .NET or reports |

Key paths:
- API: `src/backend/SimInventory.Api`
- Frontend: `src/frontend`
- Backend tests: `tests/SimInventory.Api.Tests`
- ML service/tests: `src/ml-service`, `tests/ml-service`
- Notebook: `notebooks/sim_cost_anomaly_demo.ipynb`
- Model and metadata: `notebooks/models/`
- CI/CD: `.github/workflows/`

## 3. Decisions that must not be reversed casually

1. Google sign-in only. Do not introduce OTP, email OTP, TOTP, MFA, company SSO or a custom company domain.
2. Administrators provision approved Google emails and manage roles and active/disabled state through the application. Do not hardcode ordinary users or roles.
3. Enforce authorization in the API; frontend visibility is not a security boundary.
4. Preserve the low-cost target: Cloudflare Pages, Render and Neon. No paid cloud dependencies without explicit approval.
5. Use synthetic data in source control, notebooks, tests and demonstrations.
6. Preserve allocation history. Returns and reassignments must not erase prior assignments.
7. Commit important business changes and their audit events in the same database transaction.
8. Audit hashes are hash-linked and tamper-evident, not absolutely immutable and not blockchain. Never rewrite history just to make verification pass.
9. Intelligence and ML are human-reviewed decision support. Never automatically suspend SIMs, change account access or take employee action based on an alert.
10. Never claim synthetic metrics represent real-world performance.
11. Use short-lived feature/fix/docs branches and PRs. Do not push substantive changes directly to `main`.
12. Do not claim deployment success without workflow or live health evidence.

## 4. Current product capabilities

The repository README documents:
- Google ID-token validation and exact normalized email matching against provisioned accounts.
- One-time bootstrap administrator configured through environment variables.
- Administrator account creation, role changes, disable and re-enable.
- Backend role and disabled-account checks on authenticated requests.
- SIM validation: required fields, unique ICCID/phone number, non-negative monthly cost.
- Local synthetic employee and SIM seeding.
- SIM allocation, return and reassignment with retained history.
- Hash-linked audit events, SHA-256 verification, append serialization and explicit recovery checkpoint workflow.
- Deterministic alerts for contract dates, inventory/assignment inconsistencies and unusually high same-operator costs.
- Authenticated operational reports and CSV export.

Routes:
- `/`: SIM inventory
- `/login`: Google sign-in
- `/employees`: employee directory
- `/allocations`: allocation, return and reassignment
- `/admin`: account administration
- `/audit`: audit events, verification and recovery checkpoint
- `/intelligence`: deterministic review flags
- `/reports`: current operational summaries and CSV export

Important API routes include `/api/auth/google`, `/api/admin/users`, `/api/sims`, `/api/allocations`, `/api/admin/audit`, `/api/admin/audit/verify`, `/api/admin/audit/recover`, `/api/intelligence/alerts` and `/api/reports/summary`. Inspect `Program.cs` for authoritative route and policy definitions.

## 5. Latest completed work: PR #10

PR #10 merged into `main` on 2026-11-06. Merge commit: `c314268fdac5b2eef0638c6aeaa834fc536b07b6`. The user confirmed successful runs.

It:
- Skips production schema bootstrap and synthetic demo seeding by default when `Database__InitializeOnStartup` is unset.
- Uses Next.js client-side links for internal navigation.
- Removes render-blocking Google Fonts CSS and uses local system font stacks.
- Fetches audit events and chain verification concurrently.
- Surfaces verification failures instead of retaining stale verification state.
- Separates recovery controls and disables them while requests run.
- Displays audit timestamps explicitly in UTC.
- Preserves original events and keeps recovery an explicit administrator action.
- Adds regression coverage for a recovery checkpoint after post-fork events.
- Makes SIM registration and its audit event part of the same database transaction.
- Corrects documentation for routes, production initialization and Render cold starts.

Do not repeat this work without evidence of regression. Render Free cold starts remain a platform limitation; application changes cannot eliminate host sleep/wake delays.

## 6. ML status: three separate components

### Colab experiment
- Notebook: `notebooks/sim_cost_anomaly_demo.ipynb`.
- Generates synthetic SIM cost records.
- Existing metadata says 600 synthetic records, 420 training and 180 test records.
- Isolation Forest v1.1.0; 250 estimators; contamination 0.05; random_state 42; scikit-learn 1.6.1.
- Features: `monthly_cost`, `expected_cost`, `cost_ratio`.
- Evaluation CSV: `notebooks/results/sim_anomaly_evaluation.csv`.
- Artifact: `notebooks/models/sim_cost_isolation_forest.joblib`.
- Metrics are synthetic demonstration results only. Inspect notebook methodology before relying on them.

### FastAPI inference prototype
- Code: `src/ml-service/app.py`; tests: `tests/ml-service/test_app.py`.
- Endpoints: `GET /health` and `POST /predict`.
- Loads the exported joblib artifact; accepts monthly cost and expected cost.
- Service tests use a deterministic stub model and test API behaviour, not predictive accuracy.
- Not deployed and not called by the .NET API.
- Must not connect directly to PostgreSQL or be exposed to browsers. Future calls should be server-to-server from the authenticated .NET API.

### Application intelligence and reports
- `/api/intelligence/alerts` and Intelligence page use deterministic, explainable .NET rules, not ML.
- Reports page shows operational database aggregates and CSV export. It does not show ML metrics or model predictions.
- SIM schema does not currently contain invoice history, usage time series or a verified expected-cost field.
- No automatic retraining, adaptive production model, deployed inference endpoint, live database scoring or ML performance reporting has been verified.

Do not conflate these components or describe prototypes as production functionality.

## 7. Immediate next workstream

After PR #10, first make the ML experiment reproducible and properly evaluated. Do not jump directly to deployment.

### Phase 1: audit and repair ML experiment
1. Read every notebook cell in order. Inspect the evaluation CSV, model metadata, artifact feature expectations, service code and tests.
2. Verify the notebook runs top-to-bottom in a fresh Python/Colab runtime without hidden state or local files.
3. Verify deterministic generation with the documented seed and that the notebook adapts to actual input dataset shape rather than assuming a fixed row count.
4. Check target leakage, train/test contamination, unrealistic synthetic distributions and evaluation validity.
5. Compare Isolation Forest with a transparent baseline. Report confusion matrix, precision, recall, F1, false-positive rate and class balance where labels permit. Explain synthetic-label limitations.
6. Ensure saved model, feature order, preprocessing, metadata, evaluation output and inference service agree.
7. Add repeatable tests for feature schema, invalid/missing data, model loading and prediction response.
8. Update docs with exact run steps and measured results. Never invent metrics.

### Phase 2: decide whether integration is justified
Proceed only if Phase 1 demonstrates useful value beyond the baseline. Define an approved expected-cost baseline; do not assume monthly SIM cost is a verified invoice or expected price. Define human review, model versioning, evaluation gates, rollback and drift monitoring. Do not retrain silently on unreviewed predictions or automatically replace a model.

### Phase 3: integrate only if justified
- Keep FastAPI isolated and server-side.
- Add authenticated .NET-to-ML communication, timeouts and clear unavailable-service behaviour. No browser-to-ML calls.
- Expose model version, evaluation date, dataset size/classification and metrics in a clearly labelled model-performance section of Reports or a dedicated view.
- Keep operational report aggregates separate from model evaluation metrics.
- A model score is not proof of fraud, overbilling or employee misconduct.

### Other important remaining work
- Replace EF Core `EnsureCreated` and startup SQL bootstrap with reviewed, versioned migrations before production or real-data use.
- Review browser sessionStorage JWT handling; secure HttpOnly cookies and full XSS/CSRF review remain outstanding.
- Add API integration and browser end-to-end tests. CI unit/schema tests do not prove all user journeys.
- Complete the manual acceptance matrix in `docs/TESTING.md` using observed results and evidence.
- Review database least privilege, independent append-only audit archival, monitoring, backups and restore drills before production.
- Review CORS, rate limiting behind host proxy, privacy, retention and operational security before real-data use.

## 8. Local verification commands

Backend:
```bash
dotnet restore src/backend/SimInventory.Api/SimInventory.Api.csproj
dotnet build src/backend/SimInventory.Api/SimInventory.Api.csproj --configuration Release
dotnet test tests/SimInventory.Api.Tests/SimInventory.Api.Tests.csproj --configuration Release
```

Frontend:
```bash
cd src/frontend
npm install
npm run lint
npm run build
```

ML service:
```bash
python -m pip install -r src/ml-service/requirements-dev.txt
python -m pytest tests/ml-service -v
```

Run the notebook from first to last cell in a fresh Colab runtime. Keep generated data synthetic.

## 9. Deployment and environment cautions

- API: Render Web Service; root directory `src/backend/SimInventory.Api`; Dockerfile `Dockerfile`; port 10000.
- Frontend: Cloudflare Pages static export.
- Database: Neon PostgreSQL database `sim_inventory`; local development uses Docker Compose PostgreSQL.
- Deployment is gated by `DEPLOY_ENABLED=true` and documented secrets/variables.
- API secrets belong only in Render environment variables. Never commit them or put them in `NEXT_PUBLIC_*`.
- Production should leave `Database__InitializeOnStartup` unset or false. Local Development initialization defaults to enabled.
- Render Free may sleep after inactivity. A slow first request after idle time is not automatically a code regression.
- Do not enable startup schema creation against a populated production database.

Consult `docs/DEPLOYMENT.md`, `docs/DEVELOPMENT.md` and `docs/SECURITY.md` before changing deployment behaviour.

## 10. Required agent workflow

Before coding:
1. Read this file and relevant docs.
2. Inspect current branch, working tree, recent commits and implementation. Do not assume old chat summaries are current.
3. Define the smallest coherent change and acceptance criteria.
4. Check whether the feature/fix already exists.

While coding:
1. Preserve architecture and business rules.
2. Avoid dependencies/services without a concrete need.
3. Never commit secrets or real personal/organizational data.
4. Add/update tests for changed behaviour.
5. Update docs and this handover when status changes.

Before a PR:
1. Run relevant tests/builds and state exactly what ran and passed.
2. Inspect diff for accidental files, generated artifacts, secrets, unrelated refactors and filler.
3. Summarize changed files, risks, limitations and manual steps.
4. Open PR against `main`. Do not merge unless the user explicitly authorizes it.

## 11. Documentation index

- `README.md`: overview, capabilities, routes and local setup.
- `docs/TECHNICAL_SPECIFICATION.md`: scope and acceptance criteria.
- `docs/SECURITY.md`: identity, roles, audit and privacy.
- `docs/DEPLOYMENT.md`: hosting and environment setup.
- `docs/DEVELOPMENT.md`: local development.
- `docs/REPORTING.md`: report definitions and limitations.
- `docs/ML_PROTOTYPE.md`: Colab experiment and ML integration constraints.
- `docs/TESTING.md`: automated checks and manual acceptance matrix.
- `docs/AI_ASSISTANCE.md`: AI-assisted development disclosure.


## 12. ML pipeline implementation status (2026-11-06)

PR #12 (`feat/reproducible-ml-evaluation`) is merged into `main` as commit `021f85bdc5253e931968ae0ee44433f55da50bc3`. The user confirmed both PR #11 and PR #12 were merged and all checks passed. GitHub Actions CI and Deploy workflows for this main commit both completed successfully.

The merged implementation:
- Adds `notebooks/sim_cost_model.py` as the shared, versioned training entry point.
- Uses deterministic synthetic data by default; supports an optional approved, de-identified CSV with required numeric `monthly_cost` and `expected_cost` fields.
- Uses a stratified held-out test set for labelled data and reports precision, recall, F1, confusion counts and false-positive rate for Isolation Forest and a cost-ratio baseline.
- Suppresses classification metrics for unlabelled data.
- Saves the Isolation Forest artifact and adjacent JSON metadata, including model version, feature order, configuration, data classification, record counts, dataset fingerprint and evaluation metrics.
- Exports a held-out evaluation CSV and run summary.
- Updates the separate FastAPI prototype to read model version and data classification from adjacent metadata and validate feature schema.
- Adds training-pipeline tests for reproducibility, serialization, metadata and invalid inputs.

Do not interpret merged CI as proof of predictive accuracy. It establishes automated build/test success only. The model is still experimental, synthetic-first, not deployed and not integrated into .NET or Reports.

## 13. Immediate next action: fresh Colab validation

Notebook: `notebooks/sim_cost_anomaly_demo.ipynb`
Training script: `notebooks/sim_cost_model.py`
Documentation: `docs/ML_PROTOTYPE.md`

The next agent must guide the user through a fresh Google Colab runtime, using synthetic data only:
1. Open the notebook from the `main` branch and choose Runtime > Disconnect and delete runtime, then reconnect.
2. Run every cell from top to bottom. The notebook installs `src/ml-service/requirements-dev.txt`, clones the current repository, trains the model and saves outputs.
3. Inspect metadata and evaluation CSV. Confirm dataset count, 420/180 split for default 600 rows, model version, feature list, dataset fingerprint and non-null labelled evaluation metrics.
4. Review both confusion matrices and precision, recall, F1 and false-positive rate. Compare Isolation Forest against the baseline. Do not assume Isolation Forest must win; interpret results and class balance.
5. Run the notebook's inference verification cell. It imports the FastAPI app against the newly generated artifact, checks model deserialization, schema and metadata, then calls the prediction function locally.
6. Run the ML pytest suite from the notebook. It must complete successfully.
7. Download the single `sim_cost_anomaly_outputs.zip` bundle. It contains the four generated outputs (`sim_cost_isolation_forest.joblib`, `sim_cost_model_metadata.json`, `sim_anomaly_evaluation.csv`, `run_summary.json`) plus `artifact_manifest.json` with file sizes and SHA-256 hashes.
8. Ask the user to share the notebook output or the four downloaded artifacts for review. Do not ask for organizational data.
9. Record only metrics actually observed. Do not commit model binaries or publish performance claims before reviewing results.

The Colab notebook now includes dependency installation, local inference-artifact verification and ML pytest execution. These are source-level additions; a fresh Google Colab run has not yet been observed.

### Expected default configuration

- 600 synthetic records, anomaly rate 0.05, seed 42.
- Stratified 70/30 train/test split: 420 training records and 180 held-out test records.
- Isolation Forest: 250 estimators, contamination 0.05, random_state 42.
- Model version 1.1.0.
- Features: `monthly_cost`, `expected_cost`, `cost_ratio`.
- Baseline flags cost ratios above 1.8 or below 0.35.
- Output model and metadata under `notebooks/models/`; evaluation CSV and run summary under `notebooks/results/`.

### After Colab validation

Only after inspecting real run results, decide whether the model provides enough value beyond the deterministic baseline to justify further work. Do not start production ML integration by default. The current .NET Intelligence page still uses deterministic rules; the Reports page still contains operational aggregates and CSV export, not ML evaluation metrics. The SIM schema does not have a verified expected-cost field or invoice history.

If integration is justified, design a separately approved phase with server-to-server .NET-to-FastAPI calls, authentication, timeouts, safe unavailable-service behaviour, human review, versioning, rollback and monitoring. Never connect the ML service directly to PostgreSQL or expose it to browsers.

## 14. Active handover status (2026-11-06)

- PRs #11, #12 and #13 are merged. PR #13 merge commit: `2a48ee7fd6f13b1368b97638931042de275ff0df`.
- PR #13 added pinned dependency installation, local artifact load/inference verification and ML pytest execution to the Colab notebook.
- The notebook now downloads one ZIP bundle containing the model, metadata, evaluation CSV, run summary and a SHA-256 manifest.
- Fresh-runtime Colab execution is still the outstanding user-side validation. No one needs to recreate the training pipeline or edit repository code for this step.
- Never repeat merged ML pipeline implementation. Read this file and inspect `main` before proceeding.
