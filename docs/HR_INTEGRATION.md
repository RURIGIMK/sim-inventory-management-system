# HR directory integration

The employee directory is read-only in the application. People enter, change and leave it only through the
organisation's HR system of record. This document is the integration contract for the HR team (Kenya Airways
in the collaborative deployment). Every path below feeds one sync engine
(`src/backend/SimInventory.Api/Integration/EmployeeDirectorySync.cs`), so validation, auditing and safety
rules are identical whichever path is used.

| Path | Who starts it | Use it for |
|---|---|---|
| Signed push (webhook) | The HR system, on every change | Near real-time joiners, movers and leavers |
| Scheduled pull | `hr-sync` GitHub Actions workflow, nightly | A full snapshot that catches anything a push missed |
| Pull on demand | Administrator, **Sync from HR now** on the Employees page | After an HR fix, before an audit |
| CSV upload | Administrator, Employees page | Before the HR API is connected, or as a fallback |

## Record contract

```json
{
  "mode": "snapshot",
  "employees": [
    {
      "employeeNumber": "EMP-10013",
      "fullName": "Wanjiru Kamau",
      "email": "wanjiru.kamau@example.com",
      "department": "Operations",
      "costCentre": "OPS-014",
      "status": "active"
    }
  ]
}
```

* `employeeNumber` is the key. It must be stable for the life of the employment.
* `status` accepts `active`/`employed`/`current`, `inactive`/`on_leave`/`suspended`/`secondment`, and
  `terminated`/`separated`/`resigned`/`retired`/`left`/`exited`. Anything else is rejected, never guessed.
* `mode: "snapshot"` means the list is the complete workforce: anyone active in the directory but missing from it is
  set to Inactive. `mode: "delta"` only adds and updates the people listed.
* Required: `employeeNumber`, `fullName`, `department`, `status`. Invalid rows are rejected and reported; valid rows
  in the same payload are still applied.
* Safety stop: a snapshot that would deactivate more than 30% of active staff is refused as a probable truncated export.
  Re-send the full export, or set `"allowLargeDeactivation": true` after verifying the change is real.

The CSV format uses the same fields: `employee_number, full_name, email, department, cost_centre, status`.

## Signed push (webhook)

`POST {API}/api/integrations/hr/employees` with the JSON body above and two headers:

```
X-Hr-Timestamp: <unix seconds>
X-Hr-Signature: sha256=<hex HMAC-SHA256(secret, timestamp + "." + raw body)>
```

Requests older or newer than 5 minutes are rejected, so a captured request cannot be replayed. Example (bash):

```bash
ts=$(date +%s)
sig="sha256=$(printf '%s.%s' "$ts" "$body" | openssl dgst -sha256 -hmac "$HR_WEBHOOK_SECRET" -hex | sed 's/^.*= //')"
curl -X POST "$API/api/integrations/hr/employees" -H 'Content-Type: application/json' \
  -H "X-Hr-Timestamp: $ts" -H "X-Hr-Signature: $sig" -d "$body"
```

## Scheduled pull

The API calls `GET {Hr:DirectoryUrl}` (HTTPS only) and expects the JSON contract above; the result is always treated
as a snapshot. If the HR API needs a key, the API sends `Hr:ApiKey` in the header named by `Hr:ApiKeyHeader`
(default `X-Api-Key`).

## Configuration (API environment variables on Render)

| Variable | Purpose |
|---|---|
| `Hr__WebhookSecret` | Shared HMAC secret for pushes, at least 32 random characters |
| `Hr__DirectoryUrl` | HTTPS endpoint returning the full directory |
| `Hr__ApiKey`, `Hr__ApiKeyHeader` | Optional credentials for the HR API |
| `Integration__Token` | Token the `hr-sync` workflow presents (`X-Integration-Token`) |

GitHub: secret `SIM_INTEGRATION_TOKEN` (same value as `Integration__Token`), variable `HR_SYNC_ENABLED=true`.

## What happens when someone leaves

1. The push or snapshot marks the employee Inactive or Terminated and records `employee_status_changed` in the audit log,
   noting whether they still hold a SIM.
2. The sync response, the Employees page and the `INACTIVE_EMPLOYEE_ASSIGNMENT` intelligence alert list every SIM still
   held by a leaver.
3. A manager or administrator recovers or reassigns the SIM. The application never returns or suspends a SIM on its own.

## Privacy

Only the fields above are stored. No HR attributes beyond what SIM custody needs (no salary, ID numbers or personal
contacts) are requested or accepted. The demonstration directory uses fictional names.
