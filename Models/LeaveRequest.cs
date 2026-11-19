namespace Workforce_Flow.Models;

public enum LeaveRequestStatus
{
    Submitted,
    Approved,
    Rejected,
    Escalated,
    Abandoned
}

public class LeaveRequest
{
    public int Id { get; set; }
    public Guid IdempotencyKey { get; set; }
    public int EmployeeId { get; set; }
    public int? ApproverId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public LeaveRequestStatus Status { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public DateTime? AbandonedAt { get; set; }
}