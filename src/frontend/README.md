# Frontend

Next.js 15 App Router and TypeScript interface, exported as static assets for Cloudflare Pages.

## Run locally

```bash
cd src/frontend
cp .env.example .env.local
npm install
npm run dev
```

Open http://localhost:3000.

Set `NEXT_PUBLIC_API_BASE_URL` to the ASP.NET API URL and `NEXT_PUBLIC_GOOGLE_CLIENT_ID` to the Google Web OAuth client ID. The browser stores the short-lived application JWT in session storage and sends it to the API as a bearer token.

## Implemented routes

- `/`: SIM inventory, search, status filtering and SIM registration.
- `/login`: Google sign-in.
- `/employees`: employee directory.
- `/allocations`: allocation, return and reassignment workflows.
- `/admin`: administrator account provisioning and role/status management.
- `/audit`: audit event review, chain verification and recovery checkpoint workflow.
- `/intelligence`: deterministic SIM asset review flags.
- `/reports`: inventory, allocation and cost-centre reports with CSV export.

Internal route navigation uses Next.js links to avoid unnecessary full document reloads. API data is fetched after the route loads and is not embedded in the static export.

## Deployment

The project uses `output: "export"`. Build with the production API and Google client ID supplied as public build-time environment variables:

```bash
NEXT_PUBLIC_API_BASE_URL=https://your-api.example.com NEXT_PUBLIC_GOOGLE_CLIENT_ID=your-client-id npm run build
```

Do not place API secrets, database credentials or JWT signing keys in `NEXT_PUBLIC_*` variables. Those values are visible to every browser user.
