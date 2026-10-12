# Initial Testing and Evaluation

## Scope of this evidence

This document separates automated tests already present in the repository from manual acceptance checks that should be run on the developer's machine. No result is marked as passed unless the test has actually been executed and observed.

## Automated backend tests

Project: `tests/SimInventory.Api.Tests/SimInventory.Api.Tests.csproj`

Current xUnit tests:
- `NewSimDefaultsToAvailable`: verifies that a new SIM model defaults to Available.
- `SimStoresCoreInventoryFields`: verifies that core inventory properties can be represented on the domain model.

Run from the repository root:

```powershell
dotnet test tests/SimInventory.Api.Tests/SimInventory.Api.Tests.csproj
```

Record the actual test count and result from the terminal or GitHub Actions run before presenting evidence.

## Manual integration / acceptance checks

Use synthetic records only. The integrated path requires PostgreSQL, the ASP.NET Core API and the Next.js frontend running together.

| ID | Check | Expected result | Result |
| --- | --- | --- | --- |
| INT-01 | Open `http://localhost:5080/api/health` | JSON health response with status `ok` | Pending local execution |
| INT-02 | Open frontend at `http://localhost:3000` while API is available | Connection indicator shows API connected and records are loaded from the API | Pending local execution |
| INT-03 | Register a SIM with unique ICCID and phone number | API returns created response and the record appears in the inventory list | Pending local execution |
| INT-04 | Register the same ICCID again | API rejects the duplicate with HTTP 409 Conflict | Pending local execution |
| INT-05 | Register a different ICCID with an existing phone number | API rejects the duplicate with HTTP 409 Conflict | Pending local execution |
| INT-06 | Submit without ICCID or phone number through the API | API returns HTTP 400 Bad Request | Pending local execution |
| UI-01 | Search by phone number, ICCID, operator or cost centre | Only matching rows remain visible | Pending local execution |
| UI-02 | Filter by status | Only records with the selected status are displayed | Pending local execution |
| UI-03 | Stop the API and reload the frontend | Interface falls back to clearly labelled synthetic demo mode | Pending local execution |

## Initial evaluation criteria

- Data integrity: duplicate ICCID and phone number checks are present in the registration endpoint and unique database indexes.
- Workflow integration: frontend inventory listing and registration call the backend endpoints when the API is available.
- Usability: the inventory screen exposes search, status filtering, key summary values and a registration form.
- Transparency: demo-mode changes are not persisted; authentication, allocations, audit logging, reports and frontend tests remain incomplete.

## Evidence to capture for the project review

1. Screenshot of the inventory page with the connection indicator visible.
2. Screenshot of a successfully registered synthetic SIM.
3. Screenshot or API response showing duplicate registration rejected with HTTP 409.
4. Terminal output from `dotnet test`.
5. GitHub Actions run summary after it completes.

Do not label pending checks as passed. Add dates and observed results after performing each check.
