# Zero-budget deployment and environment setup

This is an academic prototype. Use synthetic records only until an organization explicitly authorizes real-data processing and a security review is complete.

## Target architecture

- Frontend: Next.js static export on Cloudflare Pages Free.
- API: .NET 8 container on Render Free Web Service.
- Database: Neon PostgreSQL Free.
- CI/CD: GitHub Actions. CI runs on pushes and pull requests. CD is gated by the repository variable `DEPLOY_ENABLED=true`.
- Local development: Docker Compose PostgreSQL and local .NET/Next.js processes.

This is a zero-cash-cost target, not a production availability guarantee. Render Free services sleep after 15 minutes without traffic and may take about a minute to wake. Free quotas and policies can change. Do not use free hosting for sensitive real employee data or production workloads.

## Neon PostgreSQL

1. Use the `sim_inventory` database on the Neon project.
2. Copy a TLS-enabled connection string privately.
3. Configure it in Render as `ConnectionStrings__DefaultConnection`.
4. Keep local Docker PostgreSQL for development. Do not point tests at the Neon database.
5. Never put the connection string in the frontend or commit it to GitHub.

The prototype currently uses EF Core `EnsureCreatedAsync`. Before any production or real-data use, replace this with reviewed EF Core migrations, backups, restore tests and a rollback plan.

## Google sign-in and administrator bootstrap

1. Use the existing Google Web OAuth client.
2. Add `http://localhost:3000` and the final Cloudflare Pages URL as Authorized JavaScript origins.
3. Configure `Authentication__GoogleClientId` in Render and `GOOGLE_OAUTH_CLIENT_ID` as a GitHub Actions secret for frontend builds.
4. Set `Authentication__BootstrapAdminEmail` in Render to the exact Google email for the first administrator.
5. The first sign-in creates that administrator only when the database has no user records. After bootstrap, all other users must be provisioned by an administrator.
6. After the bootstrap administrator signs in, they can approve additional Google email addresses in User administration. Approved users sign in directly with Google.

## Render environment variables

Configure these variables in the Render service:

- `Authentication__JwtSigningKey`: random secret at least 32 bytes.
- `ConnectionStrings__DefaultConnection`: Neon connection string.
- `Authentication__GoogleClientId`: Google Web OAuth client ID.
- `Authentication__BootstrapAdminEmail`: first administrator's exact Google email.
- `Cors__AllowedOrigins`: exact Cloudflare Pages origin, without a trailing slash.

No email provider, sender domain or OTP signing key is required.

## Render API service

1. Sign up at Render and connect the GitHub repository.
2. Create a Web Service using the repository. Set the root directory to `src/backend/SimInventory.Api` and the Dockerfile path to `Dockerfile`.
3. Choose the Free instance type.
4. Configure the service port as `10000`, matching the Docker image.
5. Add the environment variables listed above, plus `Authentication__JwtSigningKey`.
6. Set `Cors__AllowedOrigins` to the exact frontend URL once Cloudflare Pages has created it.
7. Disable Render's automatic deploy on push if you want GitHub Actions to control deployments. Create a deploy hook and save its URL as the GitHub secret `RENDER_DEPLOY_HOOK_URL`.
8. Record the API URL, such as `https://your-service.onrender.com`, and save it as the GitHub Actions variable `RENDER_API_BASE_URL`.

Render Free Web Services sleep after 15 minutes of inactivity, have limited compute and may be suspended at quota limits. The first request after sleep may be slow.

## Cloudflare Pages frontend

1. Create a Pages project named `sim-inventory-management`.
2. Use Direct Upload because GitHub Actions builds the static export and uploads it with Wrangler.
3. Create a scoped Cloudflare API token for Pages deployment.
4. Save `CLOUDFLARE_API_TOKEN` and `CLOUDFLARE_ACCOUNT_ID` as GitHub Actions secrets.
5. Save the exact Pages project name as the variable `CLOUDFLARE_PAGES_PROJECT`.
6. Add the final Pages URL to Google OAuth Authorized JavaScript origins and to Render's `Cors__AllowedOrigins`.

## GitHub Actions configuration

Repository Settings > Secrets and variables > Actions.

Secrets:

- `GOOGLE_OAUTH_CLIENT_ID`
- `CLOUDFLARE_API_TOKEN`
- `CLOUDFLARE_ACCOUNT_ID`
- `RENDER_DEPLOY_HOOK_URL`

Variables:

- `DEPLOY_ENABLED`: keep `false` until all external services are configured.
- `RENDER_API_BASE_URL`: the Render service's HTTPS URL.
- `CLOUDFLARE_PAGES_PROJECT`: exact Pages project name.
- `CORS_ALLOWED_ORIGINS`: exact Pages origin.

The Render environment variables are configured in Render's dashboard, not copied into GitHub Actions. Keep Neon credentials and signing keys out of GitHub source code and frontend build variables.

## Deploy flow

1. A push or pull request runs CI: backend build/tests, frontend audit, type-check and build.
2. Once configured, set `DEPLOY_ENABLED=true`.
3. A push to `main` or manual workflow run triggers Render's deploy hook and builds/deploys the static frontend to Cloudflare Pages.
4. Verify `https://YOUR-RENDER-SERVICE.onrender.com/api/health` and the Cloudflare Pages URL.

## Security and limitations

- Use synthetic data only.
- Never expose Neon credentials or signing keys to the frontend.
- Hash-linked audit events are tamper-evident, not absolutely immutable.
- Configure versioned database migrations, backup/restore, monitoring, session hardening and security review before any real-data deployment.
