# Frontend

Next.js 14 and TypeScript interface for SIM inventory management.

## Run locally

```powershell
cd src/frontend
Copy-Item .env.example .env.local
npm install
npm run dev
```

Open http://localhost:3000.

The interface starts with clearly labelled synthetic demo records if the API is unavailable. If the backend is reachable at `NEXT_PUBLIC_API_BASE_URL`, the inventory table reads from `GET /api/sims` and registration submits to `POST /api/sims`. Demo-mode changes are held in browser memory and are not persisted.

## Current scope

Implemented in the UI: inventory list, search, status filter, summary metrics, registration form, duplicate checks in demo mode, and API integration for listing/registering SIMs. Authentication, allocation workflows, audit logs, reporting and frontend automated tests are not implemented yet.
