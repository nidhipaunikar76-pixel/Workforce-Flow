using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Workforce_Flow.Data;
using Workforce_Flow.Models;

namespace Workforce_Flow.Infrastructure;

public class LeaveRequestAuditInterceptor(IHttpContextAccessor httpContextAccessor) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddAuditEntries(eventData.Context as WorkforceFlowDbContext);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AddAuditEntries(eventData.Context as WorkforceFlowDbContext);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddAuditEntries(WorkforceFlowDbContext? db)
    {
        if (db is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var requestEntries = db.ChangeTracker.Entries<LeaveRequest>().ToList();
        foreach (var entry in requestEntries)
        {
            string? oldStatus = null;
            var isCreated = entry.State == EntityState.Added;
            var isStatusChange = entry.State == EntityState.Modified
                && entry.Property(request => request.Status).IsModified
                && entry.Property(request => request.Status).OriginalValue != entry.Entity.Status;

            if (!isCreated && !isStatusChange)
            {
                continue;
            }

            if (isStatusChange)
            {
                oldStatus = entry.Property(request => request.Status).OriginalValue.ToString();
            }

            var actorId = httpContextAccessor.HttpContext?.Session.GetInt32("CurrentUserId")
                ?? (isCreated ? entry.Entity.EmployeeId : entry.Entity.ApproverId ?? entry.Entity.EmployeeId);

            db.AuditLogs.Add(new AuditLog
            {
                LeaveRequest = entry.Entity,
                LeaveRequestId = entry.Entity.Id,
                ActorId = actorId,
                OldStatus = oldStatus,
                NewStatus = entry.Entity.Status.ToString(),
                Timestamp = now,
                Notes = isCreated ? "Request created" : "Status changed"
            });
        }
    }
}