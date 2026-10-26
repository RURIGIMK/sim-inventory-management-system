# Testing and Evaluation Plan

## Evidence status

This document distinguishes automated checks already in the repository from manual checks that still need to be run and recorded. No manual test is marked as passed without observed evidence.

## Automated checks currently in the repository

GitHub Actions runs:
- .NET 8 restore and Release build.
- xUnit tests in `tests/SimInventory.Api.Tests`.
- Frontend dependency audit, TypeScript type-check and Next.js static build.

The automated suite now covers basic model defaults, key EF Core schema invariants (unique SIM identifiers, one active assignment per SIM, restrictive foreign keys and cost precision), and deterministic SIM Asset Intelligence rules. The intelligence tests cover unusual-cost peer comparison, insufficient comparison data, contract expiry, allocated SIMs without active assignments and assignments linked to inactive employees. These are unit/schema tests; they do not prove Google sign-in, API role enforcement, database persistence, audit-chain verification or full frontend user journeys. A green CI result must not be presented as proof that those workflows have passed.

Run backend tests from the repository root:

```bash
dotnet test tests/SimInventory.Api.Tests/SimInventory.Api.Tests.csproj --configuration Release
```

Run frontend checks:

```bash
cd src/frontend
npm install
npm run lint
npm run build
```

## Sprint III manual acceptance matrix

Use synthetic records only. Record the date, build/commit, input or conditions, expected result, actual result, pass/fail, evidence location and any corrective action for every executed test.

| ID | Workflow / condition | Expected result | Status |
| --- | --- | --- | --- |
| AUTH-01 | Sign in with configured bootstrap Google account against an empty database | Account is created as the first active administrator | Not yet evidenced |
| AUTH-02 | Sign in with an unknown Google account after bootstrap | API rejects access and returns no application session | Not yet evidenced |
| AUTH-03 | Sign in with an approved, active account | A session is returned and the user reaches the application | Not yet evidenced |
| AUTH-04 | Sign in with a disabled account | API rejects sign-in | Not yet evidenced |
| AUTH-05 | Use an existing token after the account is disabled | Protected API request is rejected | Not yet evidenced |
| AUTH-06 | Viewer attempts a manager or administrator operation | API rejects the request even if the UI control is bypassed | Not yet evidenced |
| USER-01 | Administrator creates an account and changes its role | Account is persisted, and the new role is enforced by the API | Not yet evidenced |
| SIM-01 | Register a SIM with unique identifiers and valid cost | Record persists and appears in the inventory view | Not yet evidenced |
| SIM-02 | Reuse an ICCID or phone number, or submit a negative cost | API rejects invalid data with a clear response | Not yet evidenced |
| ALLOC-01 | Allocate an available SIM to an active employee | Allocation persists and SIM is no longer available for another active allocation | Not yet evidenced |
| ALLOC-02 | Try to allocate a SIM that already has an active allocation | API rejects the conflicting allocation | Not yet evidenced |
| ALLOC-03 | Return or reassign an allocated SIM | Assignment history remains available and current state is correct | Not yet evidenced |
| EMP-01 | Attempt to deactivate an employee with an active SIM assignment | API rejects the operation and explains that the SIM must be returned first | Not yet evidenced |
| AUD-01 | Perform an account, SIM, employee or allocation change | An audit event records the actor, action, entity and timestamp | Not yet evidenced |
| AUD-02 | Run audit-chain verification on an intact chain | Verification reports a valid chain | Not yet evidenced |\n| INTEL-01 | Create enough same-operator peers and one unusually expensive SIM | Cost rule raises a flag with observed cost, peer median and review action | Unit-tested; integration not yet evidenced |\n| INTEL-02 | Provide fewer than four same-operator peers | Cost comparison is skipped rather than making an unsupported claim | Unit-tested; integration not yet evidenced |\n| INTEL-03 | SIM contract ends within 30 days or is overdue | Contract review flag shows the date and recommended action | Unit-tested; integration not yet evidenced |\n| INTEL-04 | SIM is marked Allocated without an active assignment | Inventory consistency flag is raised | Unit-tested; integration not yet evidenced |\n| INTEL-05 | Active assignment references an inactive employee | Inventory consistency flag is raised | Unit-tested; integration not yet evidenced |
| UI-01 | Use search, filters and navigation on desktop and mobile-sized layouts | Results and controls remain usable and understandable | Not yet evidenced |
| FAIL-01 | Stop or make the API unavailable, then use the frontend | The interface shows a clear service error and does not imply failed changes were saved | Not yet evidenced |
| DEP-01 | Open the deployed frontend and API health endpoint | Both are reachable over HTTPS and the frontend can communicate with the API | Not yet evidenced |

Do not test tamper detection by modifying the live database. If demonstrating detection, use an isolated disposable test database or a dedicated test fixture.

## Initial evaluation and evidence to collect

For the information-system evaluation, prioritise:
- Completion of the main user tasks: approved sign-in, SIM registration, allocation, return/reassignment and audit review.
- Data integrity: duplicate identifiers, invalid costs, invalid state transitions and persistence after refresh.\n- Intelligence usefulness: check alert explanations against source records, validate the four-peer minimum for cost comparisons, and record false positives and missed issues using synthetic test fixtures.
- Access control: unknown/disabled users and attempts to perform operations outside the user's role.
- Usability: task clarity, error recovery and responsive layout. Record observations from a small number of representative testers if approved by the supervisor.
- Deployment behaviour: HTTPS access, API/frontend connectivity, cold-start delay, service failure messaging and recovery.

Capture evidence only after each test has actually been run:
1. GitHub Actions run summary and commit SHA.
2. Successful and rejected login outcomes using test accounts.
3. SIM registration and duplicate rejection.
4. Allocation, return/reassignment and history.
5. Audit log and verification result.
6. Deployed frontend and `/api/health` endpoint.
7. A dated test matrix with actual results and evidence filenames.

## Reporting rules

- Distinguish implemented functionality from functionality that has been tested.
- Distinguish automated CI checks from manual integration and usability evaluation.
- Report actual test counts, outcomes, sample sizes and limitations.
- Never present synthetic data or a proposed ML experiment as real organisational evidence.
