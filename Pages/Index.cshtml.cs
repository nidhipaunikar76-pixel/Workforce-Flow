using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Workforce_Flow.Data;
using Workforce_Flow.Services;

namespace Workforce_Flow.Pages;

public class IndexModel(WorkforceFlowDbContext db, INotificationService notifications) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Dashboard");

    public async Task<IActionResult> OnPostSwitchContextAsync(int userId, bool simulateFailure)
    {
        if (await db.Users.AnyAsync(user => user.Id == userId))
        {
            HttpContext.Session.SetInt32("CurrentUserId", userId);
        }

        notifications.SimulateFailure = simulateFailure;
        return RedirectToPage("/Dashboard");

    }
}
