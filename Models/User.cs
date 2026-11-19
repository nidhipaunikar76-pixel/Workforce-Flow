namespace Workforce_Flow.Models;

public enum UserRole
{
    Employee,
    Manager,
    HR
}

public class User
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public UserRole Role { get; set; }
    public int? ManagerId { get; set; }
    public bool IsOutOfOffice { get; set; }
}