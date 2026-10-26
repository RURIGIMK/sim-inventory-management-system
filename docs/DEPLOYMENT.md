# Deployment and Environment Setup

This is a student prototype. Use synthetic records only until an organization explicitly authorizes real-data processing and the security review is complete.

## Target architecture

- Frontend: Next.js static export deployed to Cloudflare Pages.
- API: .NET 8 container deployed to Google Cloud Run.
- Persistent relational database: Neon PostgreSQL.
- CI: GitHub Actions on pushes and pull requests.
- CD: GitHub Actions deploys only after repository deployment configuration is explicitly enabled.
- Local development: Docker Compose PostgreSQL and local .NET/Next.js processes.

The free tiers and their limits can change. Cloud Run may require a billing-enabled Google Cloud project, and Neon or Cloudflare quotas may limit or suspend service. “Free tier” is not a guarantee of permanent zero cost.

## Neon PostgreSQL

1. Create a Neon project and database for this application.
2. Copy the pooled or direct connection string from Neon. Use the TLS-enabled connection string supplied by Neon.
3. Save it as the GitHub Actions repository secret `NEON_CONNECTION_STRING`.
4. For local API runs, set `ConnectionStrings__DefaultConnection` in your shell or local user-secrets. Do not commit the real value.
5. Keep local Docker PostgreSQL for development. Do not point tests or local experiments at the production Neon database.

The application currently calls EF Core `EnsureCreatedAsync` to make a first demo deployment easy. This is not a migration strategy. Before production or any real organizational data, replace this with reviewed, versioned EF Core migrations, a controlled migration step, backup/restore tests and a documented rollback plan.

## Google OAuth and bootstrap administrator

1. In Google Cloud Console, create an OAuth client ID for a Web application.
2. Add local JavaScript origin `http://localhost:3000` and the final Cloudflare Pages/custom-domain origin to Authorized JavaScript origins.
3. Set the same client ID in GitHub secret `GOOGLE_OAUTH_CLIENT_ID` and frontend build environment `NEXT_PUBLIC_GOOGLE_CLIENT_ID`.
4. Set `BOOTSTRAP_ADMIN_EMAIL` to the exact Google email of the initial administrator.
5. Sign in once with that account. The API creates this one initial administrator if no user record exists. Thereafter, only administrators can provision additional accounts.
6. The first sign-in requires authenticator-app TOTP enrolment. Later sign-ins require a fresh six-digit code.

Google sign-in verifies identity; it does not grant access by itself. An account must already exist in the application, except for the explicitly configured first administrator. Use a Google OAuth client owned by the project owner, not a personal test client in production.

## GitHub Actions secrets

Create these under repository Settings > Secrets and variables > Actions > Secrets:

- `GOOGLE_OAUTH_CLIENT_ID`: Google Web OAuth client ID.
- `BOOTSTRAP_ADMIN_EMAIL`: initial administrator's Google email.
- `JWT_SIGNING_KEY`: cryptographically random string, at least 32 bytes.
- `MFA_ENCRYPTION_KEY`: base64 representation of exactly 32 random bytes.
- `NEON_CONNECTION_STRING`: Neon PostgreSQL connection string.
- `GOOGLE_WORKLOAD_IDENTITY_PROVIDER`: Google Workload Identity Federation provider resource name.
- `GOOGLE_SERVICE_ACCOUNT`: deployment service account email.
- `GOOGLE_CLOUD_PROJECT`: Google Cloud project ID.
- `CLOUDFLARE_API_TOKEN`: scoped token allowed to deploy the target Pages project.
- `CLOUDFLARE_ACCOUNT_ID`: Cloudflare account ID.

Set these under repository Settings > Secrets and variables > Actions > Variables:

- `DEPLOY_ENABLED`: set to `true` only after all deployment prerequisites are configured.
- `CLOUD_RUN_REGION`: chosen Cloud Run region.
- `CLOUD_RUN_SERVICE`: service name, for example `sim-inventory-api`.
- `CLOUDFLARE_PAGES_PROJECT`: existing Pages project name.
- `CORS_ALLOWED_ORIGINS`: comma-separated exact frontend origins, without a trailing slash.
- `NEXT_PUBLIC_API_BASE_URL`: deployed Cloud Run API base URL, without a trailing slash.

Generate secrets locally rather than inventing values or committing them:

- JWT signing key: `openssl rand -hex 32` (store the output as `JWT_SIGNING_KEY`).
- MFA encryption key: `openssl rand -base64 32` (store the output as `MFA_ENCRYPTION_KEY`).

The workflow uses Google Workload Identity Federation rather than a long-lived service-account JSON key. Configure the provider to trust only this repository and the intended branch/environment, and grant only the permissions needed for Cloud Run deployment and Cloud Build.

## Deploy sequence

1. Merge reviewed changes to `main). CI runs on pull requests and pushes.
2. Configure the secrets and variables above.
3. Set `DEPLOY_ENABLED=true` only after the API URL, OAuth origins and CORS origin are correct.
4. Run the `Deploy` workflow manually once to validate the setup.
5. Subsequent pushes to `main` deploy automatically while `DEPLOY_ENABLED=true`.

Cloud Run's first deployment may need a manual one-time bootstrap for API enablement, Workload Identity Federation, service-account permissions and the Cloud Run service identity. Those actions require the project owner's Google Cloud and Cloudflare accounts.

## Operational safeguards

- Never use actual employee records, phone numbers, ICCIDs or operator credentials in CI.
- Do not place Neon credentials in frontend environment variables.
- CORS is not authentication. The API independently validates tokens and authorization.
- Keep JWT and MFA encryption keys stable across instances and deployments. Rotating them invalidates tokens or requires a planned MFA-secret re-enrolment.
- Back up Neon data and test restoration. Database retention and backup features vary by plan.
- Confirm data location, privacy requirements, retention, incident response and written authorization before any organizational pilot.
