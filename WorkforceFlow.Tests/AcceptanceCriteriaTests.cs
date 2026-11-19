using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Workforce_Flow.Data;
using Workforce_Flow.Models;
using Workforce_Flow.Services;
using Xunit.Abstractions;

namespace WorkforceFlow.Tests;

public class AcceptanceCriteriaTests(ITestOutputHelper output)
{
    [Fact]
    public async Task DuplicateSubmissionReturnsExistingRequest()
    {
        await using var app = await TestApp.CreateAsync();
        await app.SetUserAsync(app.EmployeeId);

        var first = await app.SubmitAsync(new DateTime(2026, 12, 1), new DateTime(2026, 12, 2));
        var duplicate = await app.SubmitAsync(new DateTime(2026, 12, 1), new DateTime(2026, 12, 2), first.IdempotencyKey);
        var count = await app.QueryDbAsync(db => db.LeaveRequests.CountAsync());

        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains($"Request #{first.RequestId}", duplicate.Body);
        Assert.Equal(1, count);
        output.WriteLine("PASS Duplicate Submission: repeated key returned the existing request; row count = 1.");
    }

    [Fact]
    public async Task OverlappingDatesReturnValidationError()
    {
        await using var app = await TestApp.CreateAsync();
        await app.SetUserAsync(app.EmployeeId);

        await app.SubmitAsync(new DateTime(2026, 12, 3), new DateTime(2026, 12, 7));
        var conflict = await app.SubmitAsync(new DateTime(2026, 12, 7), new DateTime(2026, 12, 9));
        var count = await app.QueryDbAsync(db => db.LeaveRequests.CountAsync());

        Assert.Equal(HttpStatusCode.OK, conflict.StatusCode);
        Assert.Contains("already have an approved or submitted request", conflict.Body);
        Assert.Equal(1, count);
        output.WriteLine("PASS Conflicting Dates: overlapping request returned a validation error and was not stored.");
    }

    [Fact]
    public async Task OutOfOfficeManagerWithoutDelegateEscalatesToHr()
    {
        await using var app = await TestApp.CreateAsync();
        await app.SetManagerRoutingAsync(managerOutOfOffice: true, activeDelegation: false);
        await app.SetUserAsync(app.EmployeeId);

        var submitted = await app.SubmitAsync(new DateTime(2026, 12, 10), new DateTime(2026, 12, 11));
        var status = await app.QueryDbAsync(async db => (await db.LeaveRequests.SingleAsync()).Status);
        await app.SetUserAsync(app.HrId);
        using var hrResponse = await app.Client.GetAsync("/HR/Exceptions");
        var hrPage = await hrResponse.Content.ReadAsStringAsync();

        Assert.Equal(LeaveRequestStatus.Escalated, status);
        Assert.Equal(HttpStatusCode.OK, hrResponse.StatusCode);
        Assert.Contains($"#{submitted.RequestId}", hrPage);
        Assert.Contains("Elliot Employee", hrPage);
        Assert.DoesNotContain("User #2", hrPage);
        output.WriteLine("PASS Unavailable Approver: request was escalated and visible in the HR queue.");
    }

    [Fact]
    public async Task UnauthorizedApprovalAndHrPageReturnForbidden()
    {
        await using var app = await TestApp.CreateAsync();
        await app.SetUserAsync(app.EmployeeId);
        var request = await app.SubmitAsync(new DateTime(2026, 12, 12), new DateTime(2026, 12, 13));

        var approvalStatus = await app.PostApprovalAsync("Approve", request.RequestId);
        await app.SetUserAsync(app.ManagerId);
        using var hrResponse = await app.Client.GetAsync("/HR/Exceptions");

        Assert.Equal(HttpStatusCode.Forbidden, approvalStatus);
        Assert.Equal(HttpStatusCode.Forbidden, hrResponse.StatusCode);
        output.WriteLine("PASS Unauthorized Access: employee approval and manager HR access both returned 403.");
    }

    [Fact]
    public async Task NotificationExceptionDoesNotRollbackApproval()
    {
        await using var app = await TestApp.CreateAsync();
        await app.SetUserAsync(app.EmployeeId);
        var request = await app.SubmitAsync(new DateTime(2026, 12, 14), new DateTime(2026, 12, 15));
        await app.SetUserAsync(app.ManagerId);
        app.Factory.NotificationControl.ThrowOnNext = true;

        var responseStatus = await app.PostApprovalAsync("Approve", request.RequestId);
        var result = await app.QueryDbAsync(async db => new
        {
            Request = await db.LeaveRequests.SingleAsync(item => item.Id == request.RequestId),
            AuditCount = await db.AuditLogs.CountAsync(log => log.LeaveRequestId == request.RequestId),
            Failure = await db.NotificationLogs.SingleAsync(log => log.LeaveRequestId == request.RequestId && log.RetryRequired)
        });

        Assert.Equal(HttpStatusCode.OK, responseStatus);
        Assert.Equal(LeaveRequestStatus.Approved, result.Request.Status);
        Assert.NotNull(result.Request.ApprovedAt);
        Assert.Equal(2, result.AuditCount);
        Assert.False(result.Failure.Delivered);
        Assert.True(result.Failure.RetryRequired);
        output.WriteLine("PASS Failed Notification: approval and audit committed; failure was logged with RetryRequired = true.");
    }

    [Fact]
    public async Task ProjectTeamsHaveRequestedStaffingMix()
    {
        await using var app = await TestApp.CreateAsync();
        var teams = await app.QueryDbAsync(async db => await db.ProjectTeams
            .Include(team => team.Memberships)
            .ToListAsync());

        Assert.Equal(3, teams.Count);
        foreach (var team in teams)
        {
            Assert.Equal(10, team.Memberships.Count);
            Assert.Equal(5, team.Memberships.Count(member => member.Role == TeamRole.Developer));
            Assert.Equal(2, team.Memberships.Count(member => member.Role == TeamRole.Tester));
            Assert.Single(team.Memberships.Where(member => member.Role == TeamRole.Lead));
            Assert.Single(team.Memberships.Where(member => member.Role == TeamRole.Manager));
            Assert.Single(team.Memberships.Where(member => member.Role == TeamRole.SolutionArchitect));
        }

        output.WriteLine("PASS Project Teams: three seeded 10-person teams each have 5 developers, 2 testers, a lead, manager, and architect.");
    }

    [Fact]
    public async Task ApprovedLeaveHighlightsRosterAndNotifiesTeamAndManagerOnStartAndResume()
    {
        await using var app = await TestApp.CreateAsync();
        var today = DateTime.Today;
        await app.SetUserAsync(app.EmployeeId);
        var request = await app.SubmitAsync(today, today);
        await app.SetUserAsync(app.ManagerId);
        Assert.Equal(HttpStatusCode.OK, await app.PostApprovalAsync("Approve", request.RequestId));

        await app.ProcessLeaveNotificationsAtAsync(DateTime.Now);
        await app.SetUserAsync(app.EmployeeId);
        var teamPage = await app.Client.GetStringAsync("/Teams");
        Assert.Contains("Elliot Employee", teamPage);
        Assert.Contains("ON LEAVE", teamPage);

        var started = await app.QueryDbAsync(db => db.NotificationLogs
            .Where(log => log.LeaveRequestId == request.RequestId && log.Type == NotificationType.LeaveStarted)
            .GroupBy(log => log.Audience)
            .ToDictionaryAsync(group => group.Key, group => group.Count()));
        Assert.Equal(8, started[NotificationAudience.Team]);
        Assert.Equal(1, started[NotificationAudience.Manager]);
        Assert.DoesNotContain($"{today:MMM d} – {today:MMM d}", teamPage);
        Assert.Single(Regex.Matches(teamPage, "class=\"team-section\""));
        Assert.DoesNotContain("PULSE", teamPage);

        await app.SetUserAsync(app.ManagerId);
        var managerPage = await app.Client.GetStringAsync("/Teams");
        Assert.Contains("Manager alert", managerPage);
        Assert.Contains($"from {today:MMM d} through {today:MMM d}", managerPage);

        await app.SetUserAsync(app.AveryId);
        var averyNotificationId = await app.QueryDbAsync(db => db.NotificationLogs
            .Where(log => log.LeaveRequestId == request.RequestId
                && log.RecipientId == app.AveryId
                && log.Type == NotificationType.LeaveStarted)
            .Select(log => log.Id)
            .SingleAsync());
        var averyPage = await app.Client.GetStringAsync("/Teams");
        Assert.Contains("unread-badge", averyPage);
        Assert.DoesNotContain($"from {today:MMM d} through {today:MMM d}", averyPage);

        await app.SetUserAsync(app.EmployeeId);
        Assert.Equal(HttpStatusCode.NotFound, await app.MarkNotificationReadAsync(averyNotificationId));
        var unauthorizedReadStatus = await app.QueryDbAsync(db => db.NotificationLogs
            .Where(log => log.Id == averyNotificationId)
            .Select(log => log.IsRead)
            .SingleAsync());
        Assert.False(unauthorizedReadStatus);

        await app.SetUserAsync(app.AveryId);
        Assert.Equal(HttpStatusCode.OK, await app.MarkNotificationReadAsync(averyNotificationId));
        var readStatus = await app.QueryDbAsync(db => db.NotificationLogs
            .Where(log => log.Id == averyNotificationId)
            .Select(log => log.IsRead)
            .SingleAsync());
        Assert.True(readStatus);
        Assert.DoesNotContain("unread-badge", await app.Client.GetStringAsync("/Teams"));

        await app.ProcessLeaveNotificationsAtAsync(today.AddDays(1));
        await app.ProcessLeaveNotificationsAtAsync(today.AddDays(1));

        var resumed = await app.QueryDbAsync(db => db.NotificationLogs
            .Where(log => log.LeaveRequestId == request.RequestId && log.Type == NotificationType.LeaveResumed)
            .GroupBy(log => log.Audience)
            .ToDictionaryAsync(group => group.Key, group => group.Count()));
        Assert.Equal(8, resumed[NotificationAudience.Team]);
        Assert.Equal(1, resumed[NotificationAudience.Manager]);
        output.WriteLine("PASS Leave Presence: scoped roster privacy, unread/read state, and idempotent start/resume alerts verified.");
    }

    [Fact]
    public async Task FiveWorkflowsReconcileAuditAndDashboardWithSqlite()
    {
        await using var app = await TestApp.CreateAsync();
        var requestIds = new List<int>();

        await app.SetUserAsync(app.EmployeeId);
        requestIds.Add((await app.SubmitAsync(new DateTime(2026, 12, 1), new DateTime(2026, 12, 2))).RequestId);
        await app.SetUserAsync(app.ManagerId);
        Assert.Equal(HttpStatusCode.OK, await app.PostApprovalAsync("Approve", requestIds[0]));

        await app.SetUserAsync(app.EmployeeId);
        requestIds.Add((await app.SubmitAsync(new DateTime(2026, 12, 4), new DateTime(2026, 12, 5))).RequestId);
        await app.SetUserAsync(app.ManagerId);
        Assert.Equal(HttpStatusCode.OK, await app.PostApprovalAsync("Reject", requestIds[1]));

        await app.SetManagerRoutingAsync(managerOutOfOffice: true, activeDelegation: false);
        await app.SetUserAsync(app.EmployeeId);
        requestIds.Add((await app.SubmitAsync(new DateTime(2026, 12, 8), new DateTime(2026, 12, 9))).RequestId);
        await app.SetUserAsync(app.HrId);
        Assert.Equal(HttpStatusCode.OK, await app.AbandonAsync(requestIds[2]));

        await app.SetManagerRoutingAsync(managerOutOfOffice: false, activeDelegation: true);
        await app.SetUserAsync(app.EmployeeId);
        requestIds.Add((await app.SubmitAsync(new DateTime(2026, 12, 11), new DateTime(2026, 12, 12))).RequestId);
        requestIds.Add((await app.SubmitAsync(new DateTime(2026, 12, 14), new DateTime(2026, 12, 15))).RequestId);

        var auditCounts = await app.QueryDbAsync(db => db.AuditLogs
            .GroupBy(log => log.LeaveRequestId)
            .ToDictionaryAsync(group => group.Key, group => group.Count()));
        var expectedAuditCounts = new[] { 2, 2, 2, 1, 1 };
        for (var index = 0; index < requestIds.Count; index++)
        {
            Assert.Equal(expectedAuditCounts[index], auditCounts[requestIds[index]]);
        }

        var rawSqlCounts = await app.ReadStatusCountsWithSqlAsync();
        using var dashboardResponse = await app.Client.GetAsync("/Dashboard");
        var dashboard = await dashboardResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
        foreach (var status in Enum.GetValues<LeaveRequestStatus>())
        {
            Assert.Equal(rawSqlCounts.GetValueOrDefault(status.ToString()), ReadDashboardCount(dashboard, status));
        }

        Assert.Equal(8, auditCounts.Values.Sum());
        output.WriteLine("PASS Audit & Dashboard: five workflows produced 8 matching audit events; dashboard totals matched raw SQLite counts.");
    }

    private static int ReadDashboardCount(string html, LeaveRequestStatus status)
    {
        var pattern = $"<article class=\"metric metric-{status.ToString().ToLowerInvariant()}\">\\s*<span class=\"metric-label\">{status}</span>\\s*<strong>(\\d+)</strong>";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"Dashboard card for {status} was not found.");
        return int.Parse(match.Groups[1].Value);
    }
}

internal sealed class WorkforceFlowTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection keepAliveConnection;

    public WorkforceFlowTestFactory()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"WorkforceFlowTests_{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        ConnectionString = connectionString;
        keepAliveConnection = new SqliteConnection(connectionString);
        keepAliveConnection.Open();
    }

    public string ConnectionString { get; }
    public NotificationTestControl NotificationControl { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WorkforceFlow"] = ConnectionString
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<INotificationService>();
            services.AddSingleton(NotificationControl);
            services.AddScoped<INotificationService, TestNotificationService>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            keepAliveConnection.Dispose();
        }
    }
}

internal sealed class NotificationTestControl
{
    public bool ThrowOnNext { get; set; }
}

internal sealed class TestNotificationService(WorkforceFlowDbContext db, NotificationTestControl control) : INotificationService
{
    public bool SimulateFailure { get; set; }

    public async Task LogAsync(int leaveRequestId, int recipientId, string message, CancellationToken cancellationToken = default)
    {
        if (control.ThrowOnNext)
        {
            control.ThrowOnNext = false;
            throw new InvalidOperationException("Simulated notification provider exception.");
        }

        db.NotificationLogs.Add(new NotificationLog
        {
            LeaveRequestId = leaveRequestId,
            RecipientId = recipientId,
            Message = message,
            CreatedAt = DateTime.UtcNow,
            Delivered = !SimulateFailure,
            RetryRequired = SimulateFailure
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class TestApp : IAsyncDisposable
{
    private TestApp(WorkforceFlowTestFactory factory, HttpClient client)
    {
        Factory = factory;
        Client = client;
    }

    public WorkforceFlowTestFactory Factory { get; }
    public HttpClient Client { get; }
    public int EmployeeId { get; private set; }
    public int ManagerId { get; private set; }
    public int HrId { get; private set; }
    public int AveryId { get; private set; }

    public static async Task<TestApp> CreateAsync()
    {
        var factory = new WorkforceFlowTestFactory();
        var app = new TestApp(factory, factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true }));
        var connectionString = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("WorkforceFlow");
        Assert.True(connectionString?.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase), "Tests must use the in-memory SQLite database.");
        app.EmployeeId = await app.QueryDbAsync(db => db.Users.Where(user => user.Name == "Elliot Employee").Select(user => user.Id).SingleAsync());
        app.ManagerId = await app.QueryDbAsync(db => db.Users.Where(user => user.Role == UserRole.Manager).OrderBy(user => user.Id).Select(user => user.Id).FirstAsync());
        app.HrId = await app.QueryDbAsync(db => db.Users.Where(user => user.Role == UserRole.HR).Select(user => user.Id).SingleAsync());
        app.AveryId = await app.QueryDbAsync(db => db.Users.Where(user => user.Name == "Avery Brooks").Select(user => user.Id).SingleAsync());
        return app;
    }

    public async Task SetUserAsync(int userId)
    {
        var html = await Client.GetStringAsync("/Requests/Create");
        using var response = await Client.PostAsync("/?handler=SwitchContext", Form(
            ("userId", userId.ToString()),
            ("simulateFailure", "false"),
            ("__RequestVerificationToken", ReadToken(html))));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public async Task SetManagerRoutingAsync(bool managerOutOfOffice, bool activeDelegation)
    {
        var html = await Client.GetStringAsync("/Dashboard");
        using var response = await Client.PostAsync("/Dashboard?handler=UpdateRouting", Form(
            ("Routing.ManagerOutOfOffice", managerOutOfOffice.ToString().ToLowerInvariant()),
            ("Routing.ActiveDelegation", activeDelegation.ToString().ToLowerInvariant()),
            ("__RequestVerificationToken", ReadToken(html))));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public async Task<SubmissionResult> SubmitAsync(DateTime startDate, DateTime endDate, Guid? key = null)
    {
        var html = await Client.GetStringAsync("/Requests/Create");
        var idempotencyKey = key ?? Guid.Parse(ReadHiddenValue(html, "Input.IdempotencyKey"));
        using var response = await Client.PostAsync("/Requests/Create", Form(
            ("Input.IdempotencyKey", idempotencyKey.ToString()),
            ("Input.StartDate", startDate.ToString("yyyy-MM-dd")),
            ("Input.EndDate", endDate.ToString("yyyy-MM-dd")),
            ("__RequestVerificationToken", ReadToken(html))));
        var body = await response.Content.ReadAsStringAsync();
        var requestId = Regex.Match(response.RequestMessage?.RequestUri?.Query ?? string.Empty, @"existing=(\d+)");
        return new SubmissionResult(idempotencyKey, requestId.Success ? int.Parse(requestId.Groups[1].Value) : 0, response.StatusCode, body);
    }

    public async Task<HttpStatusCode> PostApprovalAsync(string handler, int requestId)
    {
        var html = await Client.GetStringAsync("/Approvals");
        using var response = await Client.PostAsync($"/Approvals?handler={handler}", Form(
            ("id", requestId.ToString()),
            ("__RequestVerificationToken", ReadToken(html))));
        return response.StatusCode;
    }

    public async Task<HttpStatusCode> AbandonAsync(int requestId)
    {
        var html = await Client.GetStringAsync("/HR/Exceptions");
        using var response = await Client.PostAsync("/HR/Exceptions?handler=Abandon", Form(
            ("id", requestId.ToString()),
            ("__RequestVerificationToken", ReadToken(html))));
        return response.StatusCode;
    }

    public async Task<HttpStatusCode> MarkNotificationReadAsync(int notificationId)
    {
        var html = await Client.GetStringAsync("/Teams");
        using var response = await Client.PostAsync("/Teams?handler=MarkRead", Form(
            ("id", notificationId.ToString()),
            ("__RequestVerificationToken", ReadToken(html))));
        return response.StatusCode;
    }

    public async Task<T> QueryDbAsync<T>(Func<WorkforceFlowDbContext, Task<T>> query)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<WorkforceFlowDbContext>());
    }

    public async Task UpdateDbAsync(Func<WorkforceFlowDbContext, Task> update)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await update(scope.ServiceProvider.GetRequiredService<WorkforceFlowDbContext>());
    }

    public async Task ProcessLeaveNotificationsAtAsync(DateTime localNow)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<LeavePresenceNotificationService>();
        await dispatcher.ProcessDueNotificationsAsync(localNow);
    }

    public Task<Dictionary<string, int>> ReadStatusCountsWithSqlAsync() => QueryDbAsync(async db =>
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT Status, COUNT(*) FROM LeaveRequests GROUP BY Status";
        await using var reader = await command.ExecuteReaderAsync();
        var counts = new Dictionary<string, int>();
        while (await reader.ReadAsync())
        {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        return counts;
    });

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
    }

    private static FormUrlEncodedContent Form(params (string Name, string Value)[] values) =>
        new(values.Select(value => new KeyValuePair<string, string>(value.Name, value.Value)));

    private static string ReadToken(string html) => ReadHiddenValue(html, "__RequestVerificationToken");

    private static string ReadHiddenValue(string html, string name)
    {
        var match = Regex.Match(html, $"<input[^>]*name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"Hidden input {name} was not found.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}

internal sealed record SubmissionResult(Guid IdempotencyKey, int RequestId, HttpStatusCode StatusCode, string Body);