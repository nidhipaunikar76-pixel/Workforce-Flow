using Workforce_Flow.Data;
using Workforce_Flow.Models;

namespace Workforce_Flow.Services;

public static class NotificationServiceExtensions
{
    public static async Task LogSafelyAsync(
        this INotificationService notifications,
        WorkforceFlowDbContext db,
        int leaveRequestId,
        int recipientId,
        string message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await notifications.LogAsync(leaveRequestId, recipientId, message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            db.NotificationLogs.Add(new NotificationLog
            {
                LeaveRequestId = leaveRequestId,
                RecipientId = recipientId,
                Message = message,
                CreatedAt = DateTime.UtcNow,
                Delivered = false,
                RetryRequired = true
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }
}