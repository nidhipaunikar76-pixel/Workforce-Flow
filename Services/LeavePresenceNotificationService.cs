using Microsoft.EntityFrameworkCore;
using Workforce_Flow.Data;
using Workforce_Flow.Models;

namespace Workforce_Flow.Services;

public class LeavePresenceNotificationService(WorkforceFlowDbContext db)
{
    public async Task ProcessDueNotificationsAsync(DateTime localNow, CancellationToken cancellationToken = default)
    {
        var today = localNow.Date;
        var requests = await db.LeaveRequests
            .Where(request => request.Status == LeaveRequestStatus.Approved && request.StartDate <= today)
            .ToListAsync(cancellationToken);

        foreach (var request in requests)
        {
            var employee = await db.Users.FindAsync([request.EmployeeId], cancellationToken);
            if (employee is null)
            {
                continue;
            }

            var teamIds = await db.TeamMemberships
                .Where(membership => membership.UserId == employee.Id)
                .Select(membership => membership.TeamId)
                .ToListAsync(cancellationToken);
            var managerIds = await db.TeamMemberships
                .Where(membership => teamIds.Contains(membership.TeamId) && membership.Role == TeamRole.Manager)
                .Select(membership => membership.UserId)
                .ToListAsync(cancellationToken);
            if (employee.ManagerId.HasValue)
            {
                managerIds.Add(employee.ManagerId.Value);
            }

            var managerRecipients = managerIds.ToHashSet();
            var teammateIds = await db.TeamMemberships
                .Where(membership => teamIds.Contains(membership.TeamId) && membership.UserId != employee.Id)
                .Select(membership => membership.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var recipients = teammateIds
                .Where(userId => userId != employee.Id)
                .Distinct()
                .ToDictionary(userId => userId, _ => NotificationAudience.Team);
            foreach (var managerId in managerRecipients)
            {
                recipients[managerId] = NotificationAudience.Manager;
            }

            if (today >= request.StartDate.Date)
            {
                await NotifyAsync(request, employee.Name, recipients, NotificationType.LeaveStarted, cancellationToken);
            }

            if (today > request.EndDate.Date)
            {
                await NotifyAsync(request, employee.Name, recipients, NotificationType.LeaveResumed, cancellationToken);
            }
        }
    }

    private async Task NotifyAsync(
        LeaveRequest request,
        string employeeName,
        IReadOnlyDictionary<int, NotificationAudience> recipients,
        NotificationType type,
        CancellationToken cancellationToken)
    {
        foreach (var recipient in recipients)
        {
            var eventKey = $"leave:{request.Id}:{type}:{recipient.Key}";
            if (await db.NotificationLogs.AnyAsync(log => log.EventKey == eventKey, cancellationToken))
            {
                continue;
            }

            var message = (type, recipient.Value) switch
            {
                (NotificationType.LeaveStarted, NotificationAudience.Manager) =>
                    $"{employeeName} is on approved leave from {request.StartDate:MMM d} through {request.EndDate:MMM d}.",
                (NotificationType.LeaveStarted, _) => $"{employeeName} is on approved leave.",
                (NotificationType.LeaveResumed, NotificationAudience.Manager) =>
                    $"{employeeName} has resumed after leave ending {request.EndDate:MMM d}.",
                _ => $"{employeeName} has resumed after approved leave."
            };
            var notification = new NotificationLog
            {
                LeaveRequestId = request.Id,
                RecipientId = recipient.Key,
                Message = message,
                Type = type,
                Audience = recipient.Value,
                EventKey = eventKey,
                CreatedAt = DateTime.UtcNow,
                Delivered = true,
                RetryRequired = false
            };

            db.NotificationLogs.Add(notification);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                db.Entry(notification).State = EntityState.Detached;
                if (!await db.NotificationLogs.AnyAsync(log => log.EventKey == eventKey, cancellationToken))
                {
                    throw;
                }
            }
        }
    }
}