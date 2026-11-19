namespace Workforce_Flow.Services;

public interface INotificationService
{
    bool SimulateFailure { get; set; }
    Task LogAsync(int leaveRequestId, int recipientId, string message, CancellationToken cancellationToken = default);
}

public sealed class NotificationSettings
{
    public bool SimulateFailure { get; set; }
}