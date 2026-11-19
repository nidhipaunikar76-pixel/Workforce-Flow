namespace Workforce_Flow.Models;

public class Delegation
{
    public int Id { get; set; }
    public int ManagerId { get; set; }
    public int DelegateManagerId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}