using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Workforce_Flow.Data;
using Workforce_Flow.Models;
using Workforce_Flow.Services;

namespace Workforce_Flow.Pages.Approvals;

public class IndexModel(WorkforceFlowDbContext db, IHttpContextAccessor httpContextAccessor, INotificationService notifications) : PageModel
{
    public User? CurrentUser { get; private set; }
    public List<LeaveRequest> Requests { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public Task<IActionResult> OnPostApproveAsync(int id) => UpdateStatusAsync(id, LeaveRequestStatus.Approved);
    public Task<IActionResult> OnPostRejectAsync(int id) => UpdateStatusAsync(id, LeaveRequestStatus.Rejected);

    private async Task<IActionResult> UpdateStatusAsync(int id, LeaveRequestStatus status)
    {
        var actor = await GetCurrentUserAsync();
        var request = await db.LeaveRequests.FirstOrDefaultAsync(item => item.Id == id && item.Status == LeaveRequestStatus.Submitted);
        if (actor?.Role != UserRole.Manager || request?.ApproverId != actor.Id)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        request.Status = status;
        if (status == LeaveRequestStatus.Approved)
        {
            request.ApprovedAt = DateTime.UtcNow;
        }
        else
        {
            request.RejectedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        await notifications.LogSafelyAsync(db, request.Id, request.EmployeeId, $"Your leave request was {status.ToString().ToLowerInvariant()}.");
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        CurrentUser = await GetCurrentUserAsync();
        if (CurrentUser?.Role == UserRole.Manager)
        {
            Requests = await db.LeaveRequests
                .Where(request => request.ApproverId == CurrentUser.Id && request.Status == LeaveRequestStatus.Submitted)
                .OrderBy(request => request.StartDate)
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