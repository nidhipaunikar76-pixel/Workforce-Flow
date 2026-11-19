# WorkforceFlow MVP

See the [portfolio package](docs/portfolio/PORTFOLIO_PACKAGE.md) for the PRD, workflows, metric tree, business case, decisions, test results, and demo script.

An ASP.NET Core 8 Razor Pages demo for employee leave requests, manager approvals, HR exceptions, project-team availability, and audit history. EF Core creates a local SQLite database named `workforceflow.db` on first run and seeds three project teams with 30 members, including developers, testers, leads, managers, and solution architects.

## Run

Install the .NET 8 SDK, then run:

```powershell
dotnet run --urls http://localhost:5187
```

Open `http://localhost:5187`. The header user switcher is for local testing only; this MVP does not provide authentication or production authorization.

## Demo flows

- Submit requests as Elliot Employee at `/Requests/Create`.
- Review manager-assigned requests at `/Approvals`.
- View project rosters and your leave-update inbox at `/Teams`.
- Inspect escalations and the append-only audit log at `/HR/Exceptions`.
- View database status counts at `/Dashboard`.
- Set **Manager out of office** and **Active delegate** on `/Dashboard` to exercise delegate routing or HR escalation.
- Enable **Fail notifications** in the header to write notification rows with `RetryRequired` set, without rolling back a request.
- When approved leave starts, the member is highlighted on their team roster and teammates/managers receive separate in-app alerts. A resume alert is sent after the leave end date. The background worker checks every minute; opening `/Teams` also refreshes due events.
- Unread leave alerts appear in the header. Each recipient can mark one alert or their whole inbox read.

## Product brief

**Problem hypothesis:** Employees and project teams need a clear approval path and timely availability information when leave is requested. The MVP focuses on request integrity, manager/delegate routing, HR escalation, and team visibility.

**Privacy decision:** Teammates see that a member is on leave, but not the leave dates. The member's project manager and HR can see the dates. Team members see only their own project rosters; HR can see all rosters. Existing team notifications are sanitized at startup to follow this policy.

**Measures to validate next:** time from submission to decision, age of pending requests, escalation rate, notification retry rate, and whether the visibility policy works for employees and managers. The prototype has no user-research or production-impact results yet; define targets after establishing a baseline.

Date-only leave and delegation boundaries follow the app host's local calendar date; event timestamps are stored in UTC. Role selection is stored in the browser session; the notification failure setting is process-local. Both are demo controls, not authentication. Existing SQLite databases receive additive schema updates without deleting requests or audit history. Future schema changes should use EF Core migrations before production use. Team updates are in-app notifications, not real-time push or email; notification failures are marked for retry but are not automatically retried yet.

## Acceptance tests

Run the integration suite with per-criterion result output:

```powershell
dotnet test .\WorkforceFlow.Tests\WorkforceFlow.Tests.csproj --logger "console;verbosity=detailed"
```