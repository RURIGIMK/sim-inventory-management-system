# Coding Agent Instructions

## Project

SIM Inventory Management System: a web-based organizational SIM asset tracking, allocation and audit system.

## Architecture

- Frontend: Next.js and TypeScript
- Backend: ASP.NET Core Web API and C#
- Persistence: PostgreSQL through Entity Framework Core
- Testing: xUnit, Jest and React Testing Library
- CI: GitHub Actions

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
