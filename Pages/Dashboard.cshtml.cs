using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Workforce_Flow.Data;
using Workforce_Flow.Models;

namespace Workforce_Flow.Pages;

public class DashboardModel(WorkforceFlowDbContext db) : PageModel
{
    [BindProperty]
    public RoutingSettings Routing { get; set; } = new();

    public IReadOnlyDictionary<LeaveRequestStatus, int> Counts { get; private set; } = new Dictionary<LeaveRequestStatus, int>();
    public int Total => Counts.Values.Sum();
    public bool RoutingUpdated { get; private set; }

    public async Task OnGetAsync()
    {
        var counts = await db.LeaveRequests
            .GroupBy(request => request.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync();
        Counts = Enum.GetValues<LeaveRequestStatus>().ToDictionary(status => status, status => counts.FirstOrDefault(item => item.Status == status)?.Count ?? 0);

        var managerId = await GetEmployeeManagerIdAsync();
        if (managerId is int id)
        {
            var manager = await db.Users.FindAsync(id);
            var today = DateTime.Today;
            Routing.ManagerOutOfOffice = manager?.IsOutOfOffice ?? false;
            Routing.ActiveDelegation = await db.Delegations.AnyAsync(item =>
                item.ManagerId == id && item.StartDate <= today && item.EndDate >= today);
        }

        RoutingUpdated = TempData["RoutingUpdated"] is not null;
    }

    public async Task<IActionResult> OnPostUpdateRoutingAsync()
    {
        var managerId = await GetEmployeeManagerIdAsync();
        if (managerId is not int id)
        {
            return NotFound();
        }

        var manager = await db.Users.FindAsync(id);
        if (manager is null)
        {
            return NotFound();
        }

        manager.IsOutOfOffice = Routing.ManagerOutOfOffice;
        var today = DateTime.Today;
        var activeDelegations = await db.Delegations
            .Where(item => item.ManagerId == id && item.StartDate <= today && item.EndDate >= today)
            .ToListAsync();

        if (Routing.ActiveDelegation && activeDelegations.Count == 0)
        {
            var delegateId = await db.Users
                .Where(user => user.Role == UserRole.Manager && user.Id != id)
                .OrderBy(user => user.Id)
                .Select(user => (int?)user.Id)
                .FirstOrDefaultAsync();
            if (delegateId.HasValue)
            {
                db.Delegations.Add(new Delegation
                {
                    ManagerId = id,
                    DelegateManagerId = delegateId.Value,
                    StartDate = today,
                    EndDate = today.AddYears(1)
                });
            }
        }
        else if (!Routing.ActiveDelegation)
        {
            db.Delegations.RemoveRange(activeDelegations);
        }

        await db.SaveChangesAsync();
        TempData["RoutingUpdated"] = true;
        return RedirectToPage();
    }

    private Task<int?> GetEmployeeManagerIdAsync() => db.Users
        .Where(user => user.Role == UserRole.Employee)
        .Select(user => user.ManagerId)
        .FirstOrDefaultAsync();
}

public class RoutingSettings
{
    public bool ManagerOutOfOffice { get; set; }
    public bool ActiveDelegation { get; set; }
}