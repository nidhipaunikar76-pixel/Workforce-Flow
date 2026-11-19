using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Workforce_Flow.Data;
using Workforce_Flow.Models;
using Workforce_Flow.Services;

namespace Workforce_Flow.Pages.Teams;

public class IndexModel(
    WorkforceFlowDbContext db,
    IHttpContextAccessor httpContextAccessor,
    LeavePresenceNotificationService leaveNotifications) : PageModel
{
    public List<ProjectTeamView> Teams { get; private set; } = [];
    public List<NotificationLog> Notifications { get; private set; } = [];
    public User? CurrentUser { get; private set; }
    public int UnreadNotificationCount => Notifications.Count(notification => !notification.IsRead);

    public async Task OnGetAsync()
    {
        await leaveNotifications.ProcessDueNotificationsAsync(DateTime.Now);
        var currentUserId = httpContextAccessor.HttpContext?.Session.GetInt32("CurrentUserId");
        CurrentUser = currentUserId.HasValue
            ? await db.Users.FindAsync(currentUserId.Value)
            : await db.Users.FirstOrDefaultAsync(user => user.Role == UserRole.Employee);

        var today = DateTime.Today;
        var activeLeaveByEmployee = await db.LeaveRequests
            .Where(request => request.Status == LeaveRequestStatus.Approved
                && request.StartDate <= today
                && request.EndDate >= today)
            .ToDictionaryAsync(request => request.EmployeeId);
        var allMemberships = await db.TeamMemberships
            .Include(membership => membership.Team)
            .Include(membership => membership.User)
            .OrderBy(membership => membership.Team!.Name)
            .ThenBy(membership => membership.Role)
            .ThenBy(membership => membership.User!.Name)
            .ToListAsync();
        var visibleMemberships = CurrentUser?.Role == UserRole.HR
            ? allMemberships
            : allMemberships.Where(membership => membership.UserId == CurrentUser?.Id).ToList();

        Teams = visibleMemberships
            .GroupBy(membership => membership.Team!)
            .Select(group => new ProjectTeamView(
                group.Key.Name,
                group.Key.ProjectCode,
                group.Key.Description,
                CurrentUser?.Role == UserRole.HR || group.Any(membership =>
                    membership.UserId == CurrentUser?.Id && membership.Role == TeamRole.Manager),
                group.Select(membership => new TeamMemberView(
                    membership.User!.Id,
                    membership.User.Name,
                    membership.Role,
                    activeLeaveByEmployee.TryGetValue(membership.UserId, out var request) ? request.StartDate : null,
                    activeLeaveByEmployee.TryGetValue(membership.UserId, out request) ? request.EndDate : null)).ToList()))
            .ToList();

        if (CurrentUser is not null)
        {
            Notifications = await db.NotificationLogs
                .Where(log => log.RecipientId == CurrentUser.Id && log.Type != NotificationType.Workflow)
                .OrderByDescending(log => log.CreatedAt)
                .Take(60)
                .ToListAsync();
        }
    }

    public async Task<IActionResult> OnPostMarkReadAsync(int id)
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null)
        {
            return NotFound();
        }

        var notification = await db.NotificationLogs.FirstOrDefaultAsync(log =>
            log.Id == id && log.RecipientId == currentUser.Id && log.Type != NotificationType.Workflow);
        if (notification is null)
        {
            return NotFound();
        }

        notification.IsRead = true;
        await db.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostMarkAllReadAsync()
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null)
        {
            return NotFound();
        }

        var unreadNotifications = await db.NotificationLogs
            .Where(log => log.RecipientId == currentUser.Id && log.Type != NotificationType.Workflow && !log.IsRead)
            .ToListAsync();
        foreach (var notification in unreadNotifications)
        {
            notification.IsRead = true;
        }

        await db.SaveChangesAsync();
        return RedirectToPage();
    }

    private async Task<User?> GetCurrentUserAsync()
    {
        var currentUserId = httpContextAccessor.HttpContext?.Session.GetInt32("CurrentUserId");
        return currentUserId.HasValue
            ? await db.Users.FindAsync(currentUserId.Value)
            : await db.Users.FirstOrDefaultAsync(user => user.Role == UserRole.Employee);
    }
}

public record ProjectTeamView(string Name, string ProjectCode, string Description, bool CanSeeLeaveDates, IReadOnlyList<TeamMemberView> Members);

public record TeamMemberView(int UserId, string Name, TeamRole Role, DateTime? LeaveStart, DateTime? LeaveEnd)
{
    public bool IsOnLeave => LeaveStart.HasValue && LeaveEnd.HasValue;
}