# Coding Agent Instructions

## Project
SIM Inventory Management System for organizational SIM tracking, employee assignment, access control and audit.

## Architecture
- Frontend: Next.js 14, React and TypeScript in `src/frontend`, static export for Cloudflare Pages.
- Backend: ASP.NET Core 8 minimal API in `src/backend/SimInventory.Api`.
- Persistence: Neon PostgreSQL hosted, Docker PostgreSQL local, EF Core/Npgsql.
- Authentication: Google ID-token validation, allowlisted/provisioned application accounts and TOTP MFA.
- CI/CD: GitHub Actions CI and gated Cloud Run / Cloudflare Pages deployment.
- Tests: xUnit in `tests/SimInventory.Api.Tests`.

## Engineering rules
1. Never commit real employee information, phone numbers, ICCIDs, tokens, secrets or database connection strings.
2. Keep authorization checks on the backend.
3. New users must be provisioned by an administrator except the explicitly configured initial bootstrap administrator.
4. Every account requires authenticator-app TOTP before accessing protected API routes.
5. User roles and active/disabled state are stored in PostgreSQL and changed through administrator endpoints, never hardcoded.
6. Preserve allocation history. Do not overwrite or delete returned assignments.
7. Important state changes should write audit events.
8. Hash-linked audit data is tamper-evident, not absolute immutability. Do not call it blockchain.
9. Use synthetic data only. Do not add ML inference to operational decisions without explicit scope approval.
10. Keep the branch's CI green before merging and do not claim deployments succeeded without workflow evidence.
11. Replace EnsureCreated with reviewed EF Core migrations before production.
12. Never expose Neon or other server secrets to the frontend bundle.

## Business rules
- ICCID and phone number are unique.
- Only available SIMs can be allocated.
- A SIM cannot have two active assignments.
- Only active employees can receive SIMs.
- Return/reassignment retains historical assignment records.
- An employee cannot be marked inactive/terminated while an active SIM assignment exists.
- Disabled application users must be denied even when holding a previously issued token.
- The last active administrator cannot be disabled or demoted.

## Key docs
- `docs/TECHNICAL_SPECIFICATION.md`
- `docs/SECURITY.md`
- `docs/DEPLOYMENT.md`
- `docs/ML_PROTOTYPE.md`
