using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Workforce_Flow.Data;
using Workforce_Flow.Models;

namespace Workforce_Flow.Pages.HR;

public class ExceptionsModel(WorkforceFlowDbContext db, IHttpContextAccessor httpContextAccessor) : PageModel
{
    public User? CurrentUser { get; private set; }
    public List<LeaveRequest> EscalatedRequests { get; private set; } = [];
    public List<AuditLog> AuditEntries { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return CurrentUser?.Role == UserRole.HR ? Page() : StatusCode(StatusCodes.Status403Forbidden);
    }

    public async Task<IActionResult> OnPostAbandonAsync(int id)
    {
        var actor = await GetCurrentUserAsync();
        if (actor?.Role != UserRole.HR)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var request = await db.LeaveRequests.FirstOrDefaultAsync(item => item.Id == id && item.Status == LeaveRequestStatus.Escalated);
        if (request is not null)
        {
            request.Status = LeaveRequestStatus.Abandoned;
            request.AbandonedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        CurrentUser = await GetCurrentUserAsync();
        if (CurrentUser?.Role == UserRole.HR)
        {
            EscalatedRequests = await db.LeaveRequests
                .Where(request => request.Status == LeaveRequestStatus.Escalated)
                .OrderBy(request => request.SubmittedAt)
                .ToListAsync();
            AuditEntries = await db.AuditLogs
                .Include(log => log.Actor)
                .OrderByDescending(log => log.Timestamp)
                .Take(250)
                .ToListAsync();
        }
    }

    private async Task<User?> GetCurrentUserAsync()
    {
        var id = httpContextAccessor.HttpContext?.Session.GetInt32("CurrentUserId");
        return id.HasValue
            ? await db.Users.FindAsync(id.Value)
            : await db.Users.FirstOrDefaultAsync(user => user.Role == UserRole.Employee);
    }
}