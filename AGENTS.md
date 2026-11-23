# Agent Instructions and Project Handover

Last verified: 2026-11-23 (branch `feature/ml-loop-hr-sync-governance`)
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
| Deterministic intelligence | .NET rules in `src/backend/SimInventory.Api/Intelligence/SimAssetIntelligence.cs` and the billing rule in `Endpoints/CostIntelligenceEndpoints.cs` |
| Schema | Versioned SQL scripts in `src/backend/SimInventory.Api/Data/Migrations`, applied by `SimInventory.Api migrate` |
| HR directory | `Integration/EmployeeDirectorySync.cs`: signed webhook push, scheduled pull, CSV upload |
| Connected ML | `src/ml-pipeline` (Python), run nightly by `.github/workflows/ml-pipeline.yml` or from `notebooks/sim_cost_pipeline_live.ipynb`; talks only to `/api/integrations/ml/*` |
| Earlier ML experiment | `notebooks/sim_cost_model.py`, `notebooks/sim_cost_anomaly_demo.ipynb`, `src/ml-service` (kept for history; superseded) |

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
13. Never edit an applied migration script. Add a new numbered script; the migrator refuses changed checksums.
14. The ML pipeline never reads the database directly and never activates a model. Administrators activate and roll back.
15. Synthetic scenario labels are evaluation-only: never a feature, never shown to reviewers, never on real invoice rows (DB constraint).
16. The employee directory is fed only by the HR system of record (KQ). Do not add manual create/edit of employees.

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

## 6. ML status (2026-11-23)

The model is now connected to the running system. See `docs/ML_PIPELINE.md`.

- Billing: `SimCostRecords` (billed amount and usage per SIM per month) imported from operator invoice CSV. `Sims.MonthlyCost` is the contracted fee.
- Pipeline: `src/ml-pipeline/sim_ml` builds nine look-ahead-free features, evaluates a candidate on the most recent five months against the rule, ratio ranking with the same flag budget and the active model, registers it as a candidate and publishes scores. Artifacts use `skops` with an explicit trusted-type list.
- Labels: human reviews once 30+ exist with both outcomes; synthetic scenarios before that; never invented.
- Web: Intelligence page shows rule and model flags side by side, model metrics, reviews and model versions (activate / roll back).
- Observed synthetic result (510 SIM-months, 11 held-out anomalies): candidate precision 0.61 / recall 1.00; rule precision 1.00 / recall 0.55; dormant billed SIMs caught 4/4 by the model and 0/4 by the rule. Synthetic only.

## 7. Next work

- Configure production: GitHub secrets `SIM_DB_CONNECTION_STRING`, `SIM_INTEGRATION_TOKEN`; Render `Integration__Token`; variables `ML_PIPELINE_ENABLED`, `AUDIT_ANCHOR_ENABLED` (and `HR_SYNC_ENABLED` once KQ provides `Hr__DirectoryUrl`). Then run the `Demo data` workflow once against the demonstration database.
- Agree the HR contract in `docs/HR_INTEGRATION.md` with KQ and exchange the webhook secret.
- Formal user acceptance testing (task-based with SUS) with administrators and managers.
- Browser end-to-end tests; HttpOnly cookie sessions and XSS/CSRF review; least-privilege DB role for the API.

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


## 12. Status of branch `feature/ml-loop-hr-sync-governance` (2026-11-23)

Adds versioned migrations, D-07 fix, 409 on identity races, SIM edit/suspend/deactivate, department scoping for managers,
HR directory integration, billing history, the connected ML loop, reviews, model activation, append-only audit triggers,
audit-chain anchoring, audit paging, fictional realistic employee names, API integration tests (Testcontainers),
ML pipeline tests and an end-to-end CI job. CI on the branch is green. Not merged; production configuration in section 7
is still required before the scheduled workflows can run.
