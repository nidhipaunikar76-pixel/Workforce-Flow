namespace Workforce_Flow.Models;

public class ProjectTeam
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string ProjectCode { get; set; }
    public required string Description { get; set; }
    public List<TeamMembership> Memberships { get; set; } = [];
}