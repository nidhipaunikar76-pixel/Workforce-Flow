using Workforce_Flow.Data;
using Workforce_Flow.Models;

namespace Workforce_Flow.Services;

public class DatabaseNotificationService(WorkforceFlowDbContext db, NotificationSettings settings) : INotificationService
{
    public bool SimulateFailure
    {
        get => settings.SimulateFailure;
        set => settings.SimulateFailure = value;
    }

    public async Task LogAsync(int leaveRequestId, int recipientId, string message, CancellationToken cancellationToken = default)
    {
        var failed = SimulateFailure;
        var notification = new NotificationLog
        {
            LeaveRequestId = leaveRequestId,
            RecipientId = recipientId,
            Message = message,
            CreatedAt = DateTime.UtcNow,
            Delivered = !failed,
            RetryRequired = failed
        };

        db.NotificationLogs.Add(notification);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            db.Entry(notification).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        }
    }
}