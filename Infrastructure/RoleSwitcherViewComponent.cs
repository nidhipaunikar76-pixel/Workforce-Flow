using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Workforce_Flow.Data;
using Workforce_Flow.Models;
using Workforce_Flow.Services;

namespace Workforce_Flow.Infrastructure;

public class RoleSwitcherViewComponent(
    WorkforceFlowDbContext db,
    IHttpContextAccessor httpContextAccessor,
    INotificationService notifications) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var users = await db.Users.OrderBy(user => user.Id).ToListAsync();
        var currentUserId = httpContextAccessor.HttpContext?.Session.GetInt32("CurrentUserId");
        if (currentUserId is null && users.Count > 0)
        {
            currentUserId = users.First(user => user.Role == UserRole.Employee).Id;
        }

        var unreadCount = currentUserId.HasValue
            ? await db.NotificationLogs.CountAsync(log =>
                log.RecipientId == currentUserId.Value
                && log.Type != Workforce_Flow.Models.NotificationType.Workflow
                && !log.IsRead)
            : 0;

        return View(new RoleSwitcherViewModel(users, currentUserId, notifications.SimulateFailure, unreadCount));
    }
}

public record RoleSwitcherViewModel(IReadOnlyList<User> Users, int? CurrentUserId, bool SimulateFailure, int UnreadCount);