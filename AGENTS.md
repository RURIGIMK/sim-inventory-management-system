# Coding Agent Instructions

## Project
Academic prototype for organizational SIM asset tracking, employee assignment, lifecycle management, reporting and audit visibility. Use synthetic data only. It is not approved for production use with real employee, phone, ICCID or billing data.

## Architecture
- Frontend: Next.js 15, React 19 and TypeScript in `src/frontend`, static export for Cloudflare Pages.
- Backend: ASP.NET Core 8 minimal API in `src/backend/SimInventory.Api`.
- Persistence: Neon PostgreSQL hosted and Docker PostgreSQL locally, using EF Core/Npgsql.
- Authentication: Google sign-in with administrator-provisioned email allowlisting. No OTP, TOTP, MFA or company SSO.
- Authorization: Administrator, Departmental Manager and Viewer roles enforced by the API.
- Hosting: Render API and Cloudflare Pages frontend.
- Tests: xUnit backend tests and Python ML-service tests, plus frontend TypeScript/build checks.
- Optional ML: Google Colab research notebook and a separate FastAPI inference prototype. Neither is currently integrated into operational reports or live database scoring.

## Engineering rules
1. Never commit real employee information, phone numbers, ICCIDs, invoices, tokens, secrets or database connection strings.
2. Keep authorization checks on the backend.
3. New users must be provisioned by an administrator except the configured first bootstrap administrator.
4. Do not introduce OTP, TOTP, MFA or company SSO without explicit scope approval.
5. User roles and active/disabled state are stored in PostgreSQL and changed through administrator endpoints, never hardcoded.
6. Preserve allocation history. Do not overwrite or delete returned assignments.
7. Important state changes must be written to the audit trail in the same database transaction as the business change.
8. Audit hashes are tamper-evident, not absolutely immutable. Never call this blockchain.
9. Keep ML as human-reviewed decision support. Do not automatically suspend SIMs or take employee/account actions.
10. Do not claim real-world ML accuracy from synthetic test results. Real records require authorization, minimization and an approved expected-cost baseline.
11. Keep CI green before merging and do not claim deployment success without workflow evidence.
12. The prototype uses `EnsureCreated` and controlled SQL bootstrap for local development. Reviewed EF Core migrations are required before production or real-data use.
13. Never expose Neon credentials, JWT signing keys or server secrets to the frontend bundle.
14. Use feature branches and pull requests for coherent changes. Do not merge while required checks are failing or pending.

## Business rules
- ICCID and phone number are unique.
- Only available SIMs can be allocated.
- A SIM cannot have two active assignments.
- Only active employees can receive a SIM.
- Return/reassignment retains historical assignment records.
- An employee cannot be marked inactive/terminated while an active SIM assignment exists.
- Disabled application users must be denied even when holding a previously issued token.
- The last active administrator cannot be disabled or demoted.

## Key docs
- `docs/TECHNICAL_SPECIFICATION.md`
- `docs/SECURITY.md`
- `docs/DEPLOYMENT.md`
- `docs/REPORTING.md`
- `docs/ML_PROTOTYPE.md`
