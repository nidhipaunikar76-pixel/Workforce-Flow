using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Workforce_Flow.Data;
using Workforce_Flow.Models;
using Workforce_Flow.Services;

namespace Workforce_Flow.Pages.Requests;

public class CreateModel(WorkforceFlowDbContext db, IHttpContextAccessor httpContextAccessor, INotificationService notifications) : PageModel
{
    [BindProperty]
    public RequestInput Input { get; set; } = new();

    public User? CurrentUser { get; private set; }
    public List<LeaveRequest> Requests { get; private set; } = [];
    public string? Notice { get; private set; }

    public async Task OnGetAsync(int? existing)
    {
        Input.IdempotencyKey = Guid.NewGuid();
        if (existing.HasValue)
        {
            var request = await db.LeaveRequests.FindAsync(existing.Value);
            Notice = request is null ? null : $"Request #{request.Id} is already on file with status {request.Status}.";
        }

        await LoadPageAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var duplicate = await db.LeaveRequests.FirstOrDefaultAsync(request => request.IdempotencyKey == Input.IdempotencyKey);
        if (Input.IdempotencyKey != Guid.Empty && duplicate is not null)
        {
            return RedirectToPage(new { existing = duplicate.Id });
        }

        CurrentUser = await GetCurrentUserAsync();
        if (CurrentUser?.Role != UserRole.Employee)
        {
            ModelState.AddModelError(string.Empty, "Select an employee in the header to submit a request.");
        }

        if (Input.StartDate is null || Input.EndDate is null || Input.StartDate.Value.Date > Input.EndDate.Value.Date)
        {
            ModelState.AddModelError(string.Empty, "Choose a valid date range; the end date must be on or after the start date.");
        }

        if (Input.IdempotencyKey == Guid.Empty)
        {
            ModelState.AddModelError(string.Empty, "Refresh the form and submit again.");
        }

        if (!ModelState.IsValid || CurrentUser is null)
        {
            Input.IdempotencyKey = Input.IdempotencyKey == Guid.Empty ? Guid.NewGuid() : Input.IdempotencyKey;
            await LoadPageAsync();
            return Page();
        }

        var startDate = Input.StartDate!.Value.Date;
        var endDate = Input.EndDate!.Value.Date;
        var hasConflict = await db.LeaveRequests.AnyAsync(request =>
            request.EmployeeId == CurrentUser.Id
            && (request.Status == LeaveRequestStatus.Approved || request.Status == LeaveRequestStatus.Submitted)
            && request.StartDate <= endDate
            && request.EndDate >= startDate);

        if (hasConflict)
        {
            ModelState.AddModelError(string.Empty, "You already have an approved or submitted request that overlaps these dates.");
            await LoadPageAsync();
            return Page();
        }

        var approverId = await FindApproverAsync(CurrentUser);
        var now = DateTime.UtcNow;
        var leaveRequest = new LeaveRequest
        {
            IdempotencyKey = Input.IdempotencyKey,
            EmployeeId = CurrentUser.Id,
            ApproverId = approverId,
            StartDate = startDate,
            EndDate = endDate,
            Status = approverId is null ? LeaveRequestStatus.Escalated : LeaveRequestStatus.Submitted,
            SubmittedAt = now,
            EscalatedAt = approverId is null ? now : null
        };

        db.LeaveRequests.Add(leaveRequest);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            db.Entry(leaveRequest).State = EntityState.Detached;
            var savedDuplicate = await db.LeaveRequests.FirstOrDefaultAsync(request => request.IdempotencyKey == Input.IdempotencyKey);
            if (savedDuplicate is not null)
            {
                return RedirectToPage(new { existing = savedDuplicate.Id });
            }

            throw;
        }

        var recipientId = approverId ?? await db.Users.Where(user => user.Role == UserRole.HR).Select(user => user.Id).FirstAsync();
        await notifications.LogSafelyAsync(db, leaveRequest.Id, recipientId, $"Leave request #{leaveRequest.Id} requires attention.");
        return RedirectToPage(new { existing = leaveRequest.Id });
    }

    private async Task<User?> GetCurrentUserAsync()
    {
        var id = httpContextAccessor.HttpContext?.Session.GetInt32("CurrentUserId");
        if (id.HasValue)
        {
            return await db.Users.FindAsync(id.Value);
        }

        return await db.Users.FirstOrDefaultAsync(user => user.Role == UserRole.Employee);
    }

    private async Task<int?> FindApproverAsync(User employee)
    {
        if (employee.ManagerId is not int managerId)
        {
            return null;
        }

        var manager = await db.Users.FindAsync(managerId);
        if (manager is null || !manager.IsOutOfOffice)
        {
            return manager?.Id;
        }

        var today = DateTime.Today;
        var delegation = await db.Delegations
            .Where(item => item.ManagerId == managerId && item.StartDate.Date <= today && item.EndDate.Date >= today)
            .OrderByDescending(item => item.StartDate)
            .FirstOrDefaultAsync();
        return delegation?.DelegateManagerId;
    }

    private async Task LoadPageAsync()
    {
        CurrentUser ??= await GetCurrentUserAsync();
        if (CurrentUser is not null)
        {
            Requests = await db.LeaveRequests
                .Where(request => request.EmployeeId == CurrentUser.Id)
                .OrderByDescending(request => request.SubmittedAt)
                .ToListAsync();
        }
    }

    public class RequestInput
    {
        [Required]
        public Guid IdempotencyKey { get; set; }

        [Required, DataType(DataType.Date)]
        public DateTime? StartDate { get; set; }

        [Required, DataType(DataType.Date)]
        public DateTime? EndDate { get; set; }
    }
}