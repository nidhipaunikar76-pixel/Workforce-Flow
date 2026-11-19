namespace Workforce_Flow.Models;

public enum NotificationType
{
    Workflow,
    LeaveStarted,
    LeaveResumed
}

public enum NotificationAudience
{
    Workflow,
    Team,
    Manager
}

public class NotificationLog
{
    public int Id { get; set; }
    public int LeaveRequestId { get; set; }
    public int RecipientId { get; set; }
    public required string Message { get; set; }
    public NotificationType Type { get; set; } = NotificationType.Workflow;
    public NotificationAudience Audience { get; set; } = NotificationAudience.Workflow;
    public string? EventKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool Delivered { get; set; }
    public bool RetryRequired { get; set; }
    public bool IsRead { get; set; }
}