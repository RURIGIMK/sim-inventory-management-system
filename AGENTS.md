# Coding Agent Instructions

## Project
SIM Inventory Management System: a web-based organizational SIM asset tracking, allocation and audit system.

## Architecture
- Frontend: Next.js 14, React and TypeScript in `src/frontend`
- Backend: ASP.NET Core 8 Web API and C# in `src/backend/SimInventory.Api`
- Persistence: PostgreSQL through Entity Framework Core and Npgsql
- Backend tests: xUnit in `tests/SimInventory.Api.Tests`
- CI: GitHub Actions in `.github/workflows/ci.yml`

## Work completed so far
- Added the project README, development notes, AI-assistance disclosure, synthetic sample dataset and data documentation.
- Added an ASP.NET Core API project with a SIM domain model and status values: Available, Allocated, Suspended and Deactivated.
- Added an Entity Framework Core database context with unique indexes for ICCID and phone number and decimal precision for monthly cost.
- Added `GET /api/health`, `GET /api/sims` and `POST /api/sims` endpoints. Registration checks required ICCID/phone number and rejects duplicate ICCIDs or phone numbers before saving.
- Added an xUnit test project with initial model tests.
- Added a Next.js/TypeScript frontend with inventory summary metrics, synthetic demo records, search, status filtering and a SIM registration form.
- The frontend attempts to read inventory from `GET /api/sims` and submit registrations to `POST /api/sims` when the API is reachable. In demo mode, edits are in-memory only and are not persisted.
- Applied a restrained red, near-black, white and neutral palette informed by publicly visible Kenya Airways brand references. No airline name or logo is used in the UI. Red `#EC1C24` is a visual approximation from public references, not a verified official UI token.
- Added a GitHub Actions workflow intended to restore/build/test the backend and validate/build the frontend.

## Current limitations
- Authentication and backend role-based authorization are not implemented.
- Allocation, reassignment, return, allocation history and audit logging are not implemented.
- Dashboard analytics beyond basic inventory metrics, exports and full reporting are not implemented.
- Frontend automated tests are not configured.
- PostgreSQL migrations are not yet committed. The local setup guide uses EF Core EnsureCreated for an initial development database; move to versioned migrations before production.
- No production deployment or continuous deployment is configured.
- Do not claim that CI passes until the latest GitHub Actions run is green.
- Demo-mode frontend changes are not persisted. The API requires a reachable PostgreSQL database to register/read persistent records.

## Engineering Rules
1. Do not commit secrets, real employee information, real phone numbers or real SIM credentials.
2. Prefer small, focused commits.
3. Add or update tests when changing business logic.
4. Keep authorization checks in the backend.
5. Preserve allocation history rather than overwriting historical records.
6. Significant state changes should produce audit records.
7. Keep database migrations versioned and reproducible.
8. Do not describe planned functionality as implemented.
9. Do not introduce machine-learning components unless the project scope is explicitly changed.
10. Verify generated or suggested code before committing it.

## Important Business Rules
- A SIM should not have two active allocations at the same time.
- SIM status must remain consistent with its allocation state.
- Only authorized roles may perform sensitive actions.
- Allocation and reassignment history should remain traceable.
- Audit records should identify the actor, action, affected SIM and timestamp.

## Academic Traceability
Implementation decisions should remain consistent with the approved project proposal and Chapters 1–3. Changes to scope should be documented rather than silently introduced.
