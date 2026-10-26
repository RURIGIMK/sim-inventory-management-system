# SIM Inventory Management System

A web-based system for organizational SIM asset tracking, employee assignment, lifecycle management and audit visibility. This is an academic prototype and is not approved for production use with real employee or SIM data.

## Current architecture

| Layer | Technology |
| --- | --- |
| Frontend | Next.js 14, React, TypeScript, static export |
| API | ASP.NET Core 8 / C# |
| ORM | Entity Framework Core |
| Hosted database | Neon PostgreSQL |
| Local database | Docker PostgreSQL |
| Authentication | Google sign-in for administrator-provisioned accounts |
| MFA | Authenticator-app TOTP |
| Authorization | API-enforced Administrator, Departmental Manager and Viewer roles |
| CI | GitHub Actions build, type-check and test |
| CD | Gated GitHub Actions deployment to Render and Cloudflare Pages |
| Optional ML research | Google Colab using synthetic data only |

## Implemented in this branch

- Google ID-token validation and email matching against provisioned application accounts.
- One-time bootstrap administrator configured through environment variables.
- Email OTP after Google sign-in, with hashed, expiring codes and attempt limits.
- Administrator UI and APIs to create accounts, change roles, disable or re-enable accounts.
- Backend role authorization and disabled-account checks on authenticated requests.
- SIM inventory API protection, required fields, duplicate ICCID/phone-number checks and non-negative cost validation.
- Employee directory APIs and interface, with administrator-managed employee records and status.
- SIM allocation, return and reassignment APIs and interface, preserving assignment history.
- Hash-linked audit events and an administrator-only audit verification endpoint.
- GitHub Actions CI workflow and gated deployment workflow.
- Render Docker deployment, Cloudflare static-export configuration, Neon setup guide, security guide, technical specification and optional synthetic-data ML plan.

Check the open pull request and GitHub Actions before treating this branch as validated. No successful CI result is claimed until Actions reports green.

## Local development

Requirements: .NET 8 SDK, Node.js 20+, npm and Docker.

1. Start PostgreSQL:

       docker compose up -d postgres

2. Start the API from the repository root:

       cd src/backend/SimInventory.Api
       dotnet restore
       dotnet run

   Set `ConnectionStrings__DefaultConnection` if you are not using the local default connection. For local sign-in, configure `Authentication__GoogleClientId`, `Authentication__BootstrapAdminEmail`, `Authentication__JwtSigningKey`, `Email__ResendApiKey` and `Email__FromAddress` as environment variables. The code contains clearly marked development-only fallback signing/encryption keys, but Google sign-in still needs a valid OAuth client ID.

3. In another terminal, configure and run the frontend:

       cd src/frontend
       npm install
       npm run dev

   Set `NEXT_PUBLIC_API_BASE_URL=http://localhost:5080` if that is the API URL reported by your local launch profile, and set `NEXT_PUBLIC_GOOGLE_CLIENT_ID` to the same Google Web OAuth client ID used by the API.

4. Open `http://localhost:3000/login`. Sign in using the configured bootstrap administrator Google account. Enter the six-digit code sent to your email. Then use User administration to provision each additional account.

The API uses `EnsureCreatedAsync` for this early prototype. Before production, replace it with versioned EF Core migrations and a reviewed deployment migration process.

## Roles

| Role | Access |
| --- | --- |
| Administrator | Provision and manage accounts, manage employee records, manage SIMs, manage allocations, review and verify audit trail |
| Departmental Manager | View employees and SIMs, register SIMs, allocate, return and reassign SIMs |
| Viewer | Read-only SIM, employee and allocation views |

New accounts should normally be created as Viewer and elevated only when justified. The API prevents self-disable/self-demotion and protects the last active administrator.

## Core API routes

| Method | Route | Access |
| --- | --- | --- |
| POST | `/api/auth/google` | Public, validates Google ID token and starts MFA |
| POST | `/api/auth/mfa/setup` | MFA setup challenge |
| POST | `/api/auth/otp` | MFA challenge |
| GET | `/api/auth/me` | Authenticated session |
| GET / POST | `/api/admin/users` | Administrator |
| PUT | `/api/admin/users/{id}` | Administrator |
| GET / POST | `/api/employees`, `/api/admin/employees` | Authenticated read / administrator write |
| PUT | `/api/admin/employees/{id}/status` | Administrator |
| GET / POST | `/api/sims` | Authenticated read / manager or administrator write |
| GET / POST | `/api/allocations` | Authenticated read / manager or administrator write |
| POST | `/api/allocations/{id}/return` | Manager or administrator |
| POST | `/api/allocations/{id}/reassign` | Manager or administrator |
| GET | `/api/admin/audit` | Administrator |
| GET | `/api/admin/audit/verify` | Administrator |

## Data handling

Only synthetic data belongs in this repository. Example employee numbers, phone numbers and ICCIDs must not be associated with real people or operator accounts. Do not commit Google credentials, Neon URLs, JWT keys, MFA encryption keys, access tokens or actual organizational data.

## Deployment

Follow [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md). The deploy workflow is gated by the `DEPLOY_ENABLED` repository variable and requires secrets and variables for Google Cloud Workload Identity Federation, Cloud Run, Neon and Cloudflare Pages.

- [Technical specification and acceptance criteria](docs/TECHNICAL_SPECIFICATION.md)
- [Security design and limitations](docs/SECURITY.md)
- [Deployment and environment setup](docs/DEPLOYMENT.md)
- [Optional synthetic-data ML experiment](docs/ML_PROTOTYPE.md)
- [Colab notebook: synthetic cost anomaly experiment](notebooks/sim_cost_anomaly_demo.ipynb)
- [Testing notes](docs/TESTING.md)

## Important limitations before production

- Use reviewed, versioned database migrations instead of startup schema creation.
- Add secure HttpOnly session cookies, monitoring and a formal XSS/CSRF review.
- Configure and test database backups, restore, data retention and access controls.
- Hash-linked audit events are tamper-evident, not absolutely immutable. Add restricted database permissions and independent append-only archival for a real deployment.
- Obtain written authorization and complete privacy/security reviews before importing organizational data.
- Confirm current free-tier limits. Render free services sleep when idle and are not suitable for production workloads.
