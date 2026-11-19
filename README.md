# WorkforceFlow

WorkforceFlow is an ASP.NET Core 8 Razor Pages MVP for employee leave requests, manager approvals, HR escalations, project-team availability, and audit history. It uses EF Core with a local SQLite database (`workforceflow.db`).

## Features

- Idempotent request submission and overlapping-date validation.
- Manager and active-delegate approval routing, with HR escalation when no approver is available.
- Audit records for request creation and status changes.
- Project teams with approved-leave status, manager/team notifications, and unread/read controls.
- Dashboard counts for Submitted, Approved, Rejected, Escalated, and Abandoned requests.

## Pages

| Route | Purpose |
|---|---|
| `/Requests/Create` | Submit and track employee requests. |
| `/Approvals` | Review requests assigned to the selected manager or delegate. |
| `/HR/Exceptions` | Review escalations and the audit log. |
| `/Teams` | View project rosters, availability, and leave notifications. |
| `/Dashboard` | View request counts and configure demo routing. |

## Run

Install the .NET 8 SDK, then run:

```powershell
dotnet run --urls http://localhost:5187
```

Open `http://localhost:5187`. On first run, the app creates `workforceflow.db` and seeds three project teams with 30 demo members.

## Tests

Run the integration acceptance suite:

```powershell
dotnet test .\WorkforceFlow.Tests\WorkforceFlow.Tests.csproj --logger "console;verbosity=detailed"
```

The suite covers request integrity, routing, access restrictions, notification failures, team composition and visibility, leave-start/resume alerts, and audit/dashboard reconciliation.

## Demo Limitations

- The header role switcher is for local testing; authentication and production authorization are not implemented.
- Team alerts are in-app only. Notification failures are marked for retry but are not automatically retried.
- Leave and delegation dates use the app host's local calendar date; event timestamps are stored in UTC.
- Schema updates are additive for this MVP. Use EF Core migrations before production use.
