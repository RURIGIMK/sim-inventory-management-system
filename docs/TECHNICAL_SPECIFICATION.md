# Technical Specification and Acceptance Criteria

## Purpose

Provide a web-based organizational SIM asset register with controlled access, traceable changes, and a path to allocation, reporting and optional analytics. This is a prototype, not an approved production system for any named organization.

## Architecture

- Frontend: Next.js, React, TypeScript, static export for Cloudflare Pages.
- API: ASP.NET Core 8, C# and Entity Framework Core.
- Database: Neon PostgreSQL for hosted persistence; Docker PostgreSQL for local work.
- Identity: Google sign-in matched to administrator-provisioned accounts.
- Roles: Administrator, Departmental Manager, Viewer.
- CI/CD: GitHub Actions, with deployments gated by repository configuration.
- API host: Render Free Web Service for prototype deployments.
- Audit: append-only application behavior with hash-linked event records.
- Optional analytics: separate Colab proof of concept on synthetic data.

## Account acceptance criteria

- An unknown Google email is rejected unless it matches the configured bootstrap administrator email.
- A provisioned Google account must pass Google token validation and verified-email checks.
- A successful Google sign-in returns an application session only when the verified, normalized email matches an active administrator-provisioned account, or the configured bootstrap administrator when the database is empty.
- A disabled account cannot access protected APIs, including with a previously issued token.
- Administrators can create accounts, change roles, disable and re-enable accounts through the application.
- New accounts default to Viewer unless the administrator explicitly selects another role.
- The last active administrator cannot be disabled or demoted.
- Role and account changes generate audit events.

## SIM inventory acceptance criteria

- ICCID and phone number are required and unique.
- New SIMs start as Available.
- Negative monthly costs are rejected.
- Viewer users can read inventory but cannot register SIMs.
- Managers and administrators can register SIMs.
- API routes reject missing or invalid session tokens.
- Data is persisted in PostgreSQL, not just frontend state.

## Audit acceptance criteria

- Significant state-changing actions record actor, action, entity, timestamp and details.
- Each event references the prior event hash and includes its own SHA-256 hash.
- Verification detects changes to any event or broken chain link.
- The interface does not describe the audit chain as blockchain or mathematically immutable.
- Production design adds separately controlled archival and restricted database permissions.

## CI/CD acceptance criteria

- Pull requests and pushes run .NET restore/build/test and frontend type-check/build.
- A failed validation fails the workflow.
- Deployment is not attempted until `DEPLOY_ENABLED=true` and required secrets/variables are configured.
- The frontend is deployed as a static export to Cloudflare Pages.
- The API is deployed as a .NET container to Render; the static frontend is hosted on Cloudflare Pages.
- Neon credentials are passed only to the API runtime and never exposed to frontend bundles.
- No deployment workflow contains hardcoded credentials.

## Persistence and recovery acceptance criteria

- Hosted records remain in Neon beyond 30 days unless explicitly deleted or the project/database is removed under provider policy.
- Database backups and restore procedures are documented and tested before real data is used.
- EF Core versioned migrations replace startup EnsureCreated before production.
- Free-tier limits, suspension behavior, region and retention are checked against the current provider plans.

## Branching and review

- `main` contains reviewed, release-ready changes.
- Use short-lived feature branches for a coherent change.
- Pull requests must pass CI before merging.
- Use `develop` only if the project needs a persistent integration branch; do not create unused branches for appearances.

## Known non-goals / remaining work

- The current branch includes employee directory CRUD/status management, SIM allocation/return/reassignment, a basic audit viewer and hash-chain verification, account administration, authentication rate limiting and deployment scaffolding.
- Reporting exports, broader automated business-rule/security tests, secure HttpOnly session cookies, full XSS/CSRF review, reviewed versioned database migrations, backups/restore drills and formal privacy/security review remain required before a production pilot.
- Machine learning remains an optional experiment and must not be represented as a deployed capability.
