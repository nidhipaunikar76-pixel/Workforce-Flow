namespace Workforce_Flow.Models;

public enum TeamRole
{
    Developer,
    Tester,
    Lead,
    Manager,
    SolutionArchitect
}

public class TeamMembership
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public ProjectTeam? Team { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public TeamRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
}