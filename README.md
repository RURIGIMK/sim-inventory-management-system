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
- A synthetic-data Colab experiment and a separate FastAPI model-inference prototype. These are not yet connected to live application data or the Reports page.

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
| `/` | SIM inventory, search, filtering and registration |
| `/login` | Google sign-in |
| `/employees` | Employee directory |
| `/allocations` | SIM allocation, return and reassignment |
| `/admin` | Account provisioning and role/status management |
| `/audit` | Audit events, chain verification and recovery checkpoint |
| `/intelligence` | Deterministic asset review flags |
| `/reports` | Operational reports and CSV export |

## Core API routes

| Method | Route | Access |
| --- | --- | --- |
| POST | `/api/auth/google` | Public Google ID-token validation |
| GET | `/api/auth/me` | Authenticated session |
| GET / POST | `/api/admin/users` | Administrator |
| PUT | `/api/admin/users/{id}` | Administrator |
| GET | `/api/employees` | Authenticated session |
| GET / POST | `/api/sims` | Authenticated read / manager or administrator write |
| GET / POST | `/api/allocations` | Authenticated read / manager or administrator write |
| POST | `/api/allocations/{id}/return` | Manager or administrator |
| POST | `/api/allocations/{id}/reassign` | Manager or administrator |
| GET | `/api/admin/audit` | Administrator |
| GET | `/api/admin/audit/verify` | Administrator |
| POST | `/api/admin/audit/recover` | Administrator |
| GET | `/api/intelligence/alerts` | Authenticated session |
| GET | `/api/reports/summary` | Authenticated session |

## Roles

| Role | Access |
| --- | --- |
| Administrator | Provision accounts, manage inventory and allocations, view employees, reports and audit trail |
| Departmental Manager | View employees and SIMs, register SIMs, allocate, return and reassign SIMs |
| Viewer | Read-only inventory, employee and allocation views |

## Data and ML limitations

Only synthetic data belongs in this repository. The current SIM schema stores monthly cost, operator, plan type, cost centre, contract dates and status. It does not store invoice history, usage time series or a verified expected-cost field.

The Colab notebook currently evaluates a model on synthetic records. Its reported metrics are not evidence of real-world predictive performance. The exported Isolation Forest model is a prototype and its separate FastAPI service is not yet deployed or called by the .NET API. The application intelligence page currently uses deterministic rules, not ML predictions.

Before any organizational data is used, obtain written authorization, minimize the exported fields, remove employee identifiers and contact details, establish an approved expected-cost baseline, and define human review labels. Do not use predictions to automatically suspend a SIM or take employee/account action.

## Deployment

Follow [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md). The API runs on Render and the static frontend on Cloudflare Pages. The deploy workflow is gated by the `DEPLOY_ENABLED` repository variable. Neon credentials and the JWT signing key belong only in the API runtime environment.

Render Free services can sleep after inactivity and take time to wake. Code optimizations reduce application startup work and browser navigation overhead but cannot eliminate platform cold starts.

## Important limitations before production

- Replace `EnsureCreated` and startup SQL bootstrap with reviewed, versioned EF Core migrations.
- Add secure HttpOnly session cookies and complete an XSS/CSRF review.
- Configure restricted database permissions, independent append-only audit archival, alerts and tested backups/restores.
- Add full API integration and browser end-to-end tests.
- Establish approved data governance and privacy controls before using organizational records.
- Do not describe hash-linked audit events as absolutely immutable.
- Do not describe the synthetic ML experiment as a deployed or validated production model.

## Documentation

- [Technical specification](docs/TECHNICAL_SPECIFICATION.md)
- [Security design](docs/SECURITY.md)
- [Deployment and environment setup](docs/DEPLOYMENT.md)
- [Reporting scope and interpretation](docs/REPORTING.md)
- [ML prototype and limitations](docs/ML_PROTOTYPE.md)
- [Testing and evaluation plan](docs/TESTING.md)
- [Colab notebook](notebooks/sim_cost_anomaly_demo.ipynb)
