# Security Design

## Account lifecycle

- Accounts are provisioned by an administrator in the application.
- Sign-in uses Google Identity Services. The backend validates the Google ID token, audience and verified email.
- Google identity is matched to the exact normalized email already provisioned in the application.
- A configured bootstrap administrator is the sole exception needed to establish the first administrator.
- Roles are stored in PostgreSQL, not source code. Administrators can create accounts, change roles, disable or re-enable accounts without editing source.
- API authorization is enforced server-side. Hiding a frontend control is not a security boundary.
- The API checks account status on authenticated requests, so disabled users are rejected even if they still possess an unexpired token.
- The last active administrator cannot be demoted or disabled, and an administrator cannot disable their own account.
- Rejected Google sign-ins are written to the audit trail with a reason category. Submitted Google credentials are never written to the log.

## Roles

| Role | Access |
| --- | --- |
| Administrator | User administration, SIM registration, editing and status changes, billing import, HR directory sync, anomaly reviews, model activation, audit trail |
| Departmental Manager | Allocate, return and reassign SIMs for employees in their own department only; read inventory, employees, reports and intelligence |
| Viewer | Read-only inventory, employee, allocation, billing and intelligence views |

A Departmental Manager must have a department. Scoping is enforced in the API for every custody action (including both sides of a reassignment), not only in the interface. Default new accounts to Viewer.

## Machine-to-machine access

- Scheduled jobs (ML pipeline, HR pull, audit anchor) present `X-Integration-Token`, compared in constant time. The token unlocks only `/api/integrations/*`; it cannot reach user, SIM or custody routes, and user sessions cannot reach integration routes. Integration routes are rate limited.
- HR pushes are authenticated by HMAC-SHA256 over timestamp and body with a five-minute replay window.
- The ML pipeline receives no names, phone numbers, ICCIDs or emails. Model artifacts are loaded with `skops`, which refuses unexpected types, and are checksum-verified.

## Session signing and account access

- Only the JWT signing key is required for application sessions. Configure a random secret of at least 32 bytes outside source control.
- The backend validates the Google ID token, verified email and OAuth audience, then checks the normalized email against the approved account list.
- A valid Google identity without an active approved account is denied access.
- The JWT key has a development-only fallback. Never run a production deployment with development fallbacks.

## Audit trail

State-changing actions write an audit event containing actor, action, affected entity, timestamp, details, previous hash and SHA-256 hash. The chain is tamper-evident: changing an event breaks subsequent verification. It is not absolute immutability. A database owner who can rewrite all records and hashes could reconstruct the chain.

Implemented hardening: database triggers reject UPDATE, DELETE and TRUNCATE on `AuditEvents` for every role (migration `0003`), and the `audit-anchor` workflow records the verified chain head daily on the `audit-anchors` Git branch, so a history rewritten and re-hashed inside the database no longer matches the external record. Remaining before production: a separate least-privilege database role for the API (so it could not drop the triggers), independent WORM archival, alerting and tested retention. Blockchain is not required for this prototype; it would add operational complexity without replacing access control, backups or governance.

## Data and privacy

- Only synthetic data is allowed in public repository examples and automated tests.
- Do not import employee records or live operator usage/billing information without written authorization, a defined purpose, data minimization, access controls, retention rules and a privacy assessment.
- Never log raw Google credentials, JWTs or signing secrets. Audit actor emails and account identifiers are retained for accountability; avoid adding phone numbers or ICCIDs to audit details unless necessary. Define access and retention rules before real-data use.
- The API applies a basic fixed-window rate limit to Google sign-in. Before production, review per-client throttling behind the host proxy, alerting, lockout/recovery policy, monitoring, strict CORS origins and additional abuse protections.
