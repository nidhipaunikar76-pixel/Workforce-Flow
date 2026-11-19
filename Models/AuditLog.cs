namespace Workforce_Flow.Models;

public class AuditLog
{
    public int Id { get; set; }
    public int LeaveRequestId { get; set; }
    public LeaveRequest LeaveRequest { get; set; } = null!;
    public int ActorId { get; set; }
    public User Actor { get; set; } = null!;
    public string? OldStatus { get; set; }
    public required string NewStatus { get; set; }
    public DateTime Timestamp { get; set; }
    public required string Notes { get; set; }
}