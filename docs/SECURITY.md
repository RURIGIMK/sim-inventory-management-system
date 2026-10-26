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

## Roles

| Role | Access |
| --- | --- |
| Administrator | User administration, SIM inventory management and audit-log review |
| Departmental Manager | Read inventory and register SIMs |
| Viewer | Read-only inventory access |

Expand this matrix as allocation approvals, reporting and employee management are implemented. Default new accounts to Viewer.

## Session signing and account access

- Only the JWT signing key is required for application sessions. Configure a random secret of at least 32 bytes outside source control.
- The backend validates the Google ID token, verified email and OAuth audience, then checks the normalized email against the approved account list.
- A valid Google identity without an active approved account is denied access.
- The JWT key has a development-only fallback. Never run a production deployment with development fallbacks.

## Audit trail

State-changing actions write an audit event containing actor, action, affected entity, timestamp, details, previous hash and SHA-256 hash. The chain is tamper-evident: changing an event breaks subsequent verification. It is not absolute immutability. A database owner who can rewrite all records and hashes could reconstruct the chain.

Before production, strengthen the design with database roles that deny application-level update/delete on audit rows, a verification job, independent append-only/WORM archival or signed external checkpoints, alerting, and tested retention. Blockchain is not required for this prototype; it would add operational complexity without replacing access control, backups or governance.

## Data and privacy

- Only synthetic data is allowed in public repository examples and automated tests.
- Do not import employee records or live operator usage/billing information without written authorization, a defined purpose, data minimization, access controls, retention rules and a privacy assessment.
- Never put authentication tokens, phone numbers, ICCIDs or personal information in logs.
- The API applies a basic fixed-window rate limit to Google sign-in. Before production, review per-client throttling behind the host proxy, alerting, lockout/recovery policy, monitoring, strict CORS origins and additional abuse protections.
