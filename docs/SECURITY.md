# Security Design

## Account lifecycle

- Accounts are provisioned by an administrator in the application.
- Sign-in uses Google Identity Services. The backend validates the Google ID token, audience and verified email.
- Google identity is matched to the exact normalized email already provisioned in the application.
- A configured bootstrap administrator is the sole exception needed to establish the first administrator.
- Every account must enrol in authenticator-app TOTP before accessing application APIs.
- Roles are stored in PostgreSQL, not source code. Administrators can create accounts, change roles, disable or re-enable accounts without editing source.
- API authorization is enforced server-side. Hiding a frontend control is not a security boundary.
- The API checks account status on authenticated requests, so disabled users are rejected even if they still possess an unexpired token.
- The last active administrator cannot be demoted or disabled, and an administrator cannot disable their own account.

## Roles

| Role | Access |
| --- | --- |
| Administrator | User administration, SIM inventory management and audit-log review |
| Departmental Manager | Read inventory and register SIMs |
| Viewer | Read-only inventory access |

Expand this matrix as allocation approvals, reporting and employee management are implemented. Default new accounts to Viewer.

## MFA and secret storage

- TOTP codes are six digits and valid for a short time window.
- TOTP secrets are encrypted at rest with AES-256-GCM using a separate 32-byte key supplied through configuration.
- JWT signing and MFA encryption keys must be independent, random secrets and must not be committed to Git.
- Local development uses explicitly marked development-only fallback keys. Never run production with these fallbacks.
- The frontend stores the short-lived application token in sessionStorage. This reduces persistence after closing the tab but does not eliminate XSS risk. Add a secure, HttpOnly, SameSite cookie-based session before production use.

## Audit trail

State-changing actions write an audit event containing actor, action, affected entity, timestamp, details, previous hash and SHA-256 hash. The chain is tamper-evident: changing an event breaks subsequent verification. It is not absolute immutability. A database owner who can rewrite all records and hashes could reconstruct the chain.

Before production, strengthen the design with database roles that deny application-level update/delete on audit rows, a verification job, independent append-only/WORM archival or signed external checkpoints, alerting, and tested retention. Blockchain is not required for this prototype; it would add operational complexity without replacing access control, backups or governance.

## Data and privacy

- Only synthetic data is allowed in public repository examples and automated tests.
- Do not import employee records or live operator usage/billing information without written authorization, a defined purpose, data minimization, access controls, retention rules and a privacy assessment.
- Never put authentication tokens, phone numbers, ICCIDs or personal information in logs.
- Use HTTPS, strict CORS origins, input validation, rate limiting on sign-in and OTP endpoints, and monitoring before production.
