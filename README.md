# SIM Inventory Management System

A web-based system for organizational SIM asset tracking, employee assignment, lifecycle management, reporting and audit visibility. This is an academic prototype and is not approved for production use with real employee, SIM or billing data.

## Architecture

| Layer | Technology |
| --- | --- |
| Frontend | Next.js 15, React 19, TypeScript, static export |
| API | ASP.NET Core 8 / C# minimal API |
| ORM | Entity Framework Core |
| Hosted database | Neon PostgreSQL |
| Local database | Docker PostgreSQL |
| Authentication | Google sign-in with administrator-approved accounts |
| Authorization | Administrator, Departmental Manager and Viewer roles |
| API hosting | Render |
| Frontend hosting | Cloudflare Pages |
| CI | GitHub Actions |
| Optional ML research | Google Colab, scikit-learn and a separate FastAPI inference prototype |

## Implemented capabilities

- Google ID-token validation and exact normalized email matching against provisioned application accounts.
- One-time bootstrap administrator configured through environment variables.
- Administrator UI and APIs to create accounts, change roles, disable or re-enable accounts.
- Backend role authorization and disabled-account checks on authenticated requests.
- SIM inventory with required-field, duplicate ICCID/phone-number and non-negative cost validation.
- Synthetic employee directory and SIM inventory seed for local development. Production startup skips schema bootstrap and demo seeding by default when `Database__InitializeOnStartup` is unset.
- SIM allocation, return and reassignment workflows that preserve assignment history.
- Hash-linked audit events, timestamp-stable SHA-256 verification, concurrent append serialization and an explicit recovery-checkpoint workflow for the known historical fork.
- Explainable deterministic asset-intelligence rules for contract dates, inventory/assignment inconsistencies and unusually high same-operator costs.
- Authenticated reporting dashboard for inventory status, monthly committed cost, operator and cost-centre breakdowns, contract dates and allocation history, with CSV export.
- SIM lifecycle management by administrators: edit commercial details, suspend, lift suspension, deactivate (terminal), with every change audited.
- Departmental scoping: managers change custody only for employees in their own department.
- HR directory integration (signed webhook push, scheduled pull, CSV export) with leaver detection. See [docs/HR_INTEGRATION.md](docs/HR_INTEGRATION.md).
- Monthly billing history from operator invoices and an anomaly model connected to the live system: nightly retraining, time-ordered evaluation against the rule, administrator activation and rollback, and human reviews that become the evaluation labels. See [docs/ML_PIPELINE.md](docs/ML_PIPELINE.md).
- Versioned SQL migrations, database-enforced append-only audit events and a daily external anchor of the audit chain head.

## Local development

Requirements: .NET 8 SDK, Node.js 20+, npm and Docker.

1. Start PostgreSQL:

   ```bash
   docker compose up -d postgres
   ```

2. Start the API from the repository root:

   ```bash
   cd src/backend/SimInventory.Api
   dotnet restore
   dotnet run
   ```

   Local Development environment initialization is enabled by default. Configure `ConnectionStrings__DefaultConnection`, `Authentication__GoogleClientId`, `Authentication__BootstrapAdminEmail` and `Authentication__JwtSigningKey` through environment variables. Google sign-in requires a valid OAuth client ID.

3. In another terminal, configure and run the frontend:

   ```bash
   cd src/frontend
   cp .env.example .env.local
   npm install
   npm run dev
   ```

   Set `NEXT_PUBLIC_API_BASE_URL` to the API URL and `NEXT_PUBLIC_GOOGLE_CLIENT_ID` to the Google Web OAuth client ID.

4. Open `http://localhost:3000/login`. Sign in using the configured bootstrap administrator Google account. After the first administrator is created, use User administration to approve additional Google email addresses and assign their roles.

## Application routes

| Route | Purpose |
| --- | --- |
| `/` | SIM inventory, search, filtering, registration, editing and status changes |
| `/login` | Google sign-in |
| `/employees` | Employee directory and HR integration (sync, CSV import, run history, leavers holding SIMs) |
| `/allocations` | SIM allocation, return and reassignment |
| `/billing` | Operator invoice import and monthly billed-vs-contracted totals |
| `/admin` | Account provisioning and role/status management |
| `/audit` | Audit events, chain verification and recovery checkpoint |
| `/intelligence` | Cost anomalies (rule and model side by side, reviews, model versions) and deterministic inventory checks |
| `/reports` | Operational reports and CSV export |

## Core API routes

| Method | Route | Access |
| --- | --- | --- |
| POST | `/api/auth/google` | Public Google ID-token validation |
| GET | `/api/auth/me` | Authenticated session |
| GET / POST | `/api/admin/users` | Administrator |
| PUT | `/api/admin/users/{id}` | Administrator |
| GET | `/api/employees` | Authenticated session |
| GET / POST | `/api/sims` | Authenticated read / administrator write |
| PUT | `/api/sims/{id}` | Administrator: edit commercial details |
| POST | `/api/sims/{id}/status` | Administrator: suspend, lift suspension, deactivate |
| GET | `/api/sims/{id}/costs` | Authenticated session |
| GET / POST | `/api/allocations` | Authenticated read / manager or administrator write |
| POST | `/api/allocations/{id}/return` | Manager or administrator |
| POST | `/api/allocations/{id}/reassign` | Manager or administrator |
| POST | `/api/admin/employees/import` · `/sync` · GET `/sync-runs` | Administrator (HR CSV export, pull, history) |
| POST | `/api/admin/billing/import` | Administrator (operator invoice CSV) |
| GET | `/api/billing/summary` | Authenticated session |
| GET | `/api/intelligence/anomalies` | Authenticated session |
| POST | `/api/intelligence/reviews` | Administrator |
| GET / POST | `/api/ml/models` · `/api/ml/models/{id}/activate` | Authenticated read / administrator activate |
| * | `/api/integrations/ml/*`, `/api/integrations/hr/sync`, `/api/integrations/audit/head` | Integration token only |
| POST | `/api/integrations/hr/employees` | HR system, HMAC-signed |
| GET | `/api/admin/audit?beforeId=&take=` | Administrator (paged) |
| GET | `/api/admin/audit/verify` | Administrator |
| POST | `/api/admin/audit/recover` | Administrator |
| GET | `/api/intelligence/alerts` | Authenticated session |
| GET | `/api/reports/summary` | Authenticated session |

## Roles

| Role | Access |
| --- | --- |
| Administrator | Provision accounts, manage inventory and allocations, view employees, reports and audit trail |
| Departmental Manager | View employees and SIMs; allocate, return and reassign SIMs for employees in their own department |
| Viewer | Read-only inventory, employee and allocation views |

## Data and ML limitations

Only synthetic data belongs in this repository. `Sims.MonthlyCost` is the contracted monthly fee; `SimCostRecords` holds billed amounts and usage per month. Demonstration billing is synthetic and labelled; employee names in the demonstration directory are fictional.

Model metrics computed on synthetic scenarios describe the generator, not real billing. Once reviewers have confirmed or dismissed enough flags, the pipeline evaluates against those reviews instead.

Before any organizational data is used, obtain written authorization, minimize the exported fields, remove employee identifiers and contact details, establish an approved expected-cost baseline, and define human review labels. Do not use predictions to automatically suspend a SIM or take employee/account action.

## Deployment

Follow [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md). The API runs on Render and the static frontend on Cloudflare Pages. The deploy workflow is gated by the `DEPLOY_ENABLED` repository variable. Neon credentials and the JWT signing key belong only in the API runtime environment.

Render Free services can sleep after inactivity and take time to wake. Code optimizations reduce application startup work and browser navigation overhead but cannot eliminate platform cold starts.

## Important limitations before production

- Add secure HttpOnly session cookies and complete an XSS/CSRF review.
- Configure restricted database permissions, independent append-only audit archival, alerts and tested backups/restores.
- Add browser end-to-end tests (API integration and pipeline end-to-end tests run in CI).
- Establish approved data governance and privacy controls before using organizational records.
- Do not describe hash-linked audit events as absolutely immutable.
- Do not describe model results on synthetic data as validated real-world performance.

## Documentation

- [Technical specification](docs/TECHNICAL_SPECIFICATION.md)
- [Security design](docs/SECURITY.md)
- [Deployment and environment setup](docs/DEPLOYMENT.md)
- [Reporting scope and interpretation](docs/REPORTING.md)
- [Billing history and the anomaly model loop](docs/ML_PIPELINE.md)
- [HR directory integration](docs/HR_INTEGRATION.md)
- [Original ML prototype](docs/ML_PROTOTYPE.md)
- [Testing and evaluation plan](docs/TESTING.md)
- [Colab notebook connected to the live API](notebooks/sim_cost_pipeline_live.ipynb)
- [Original Colab experiment](notebooks/sim_cost_anomaly_demo.ipynb)
