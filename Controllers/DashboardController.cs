using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HRSystem.Data;
using HRSystem.Infrastructure;
using HRSystem.Models;

namespace HRSystem.Controllers;

[Authorize]
public class DashboardController(ApplicationDbContext context, UserManager<IdentityUser> userManager) : Controller
{
    public async Task<IActionResult> Index(int reportsPage = 1, int usersPage = 1)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var isAdmin = User.IsInRole(AppRoles.Admin);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var yearStart = new DateOnly(today.Year, 1, 1);

        var attendance = new AttendanceSummaryViewModel();
        if (!isAdmin)
        {
            var myLeaves = await context.LeaveRequests.AsNoTracking()
                .Where(l => l.EmployeeUserId == userId)
                .ToListAsync();

            var approved = myLeaves.Where(l => l.Status == LeaveRequestStatus.Approved).ToList();

            attendance = new AttendanceSummaryViewModel
            {
                ApprovedLeaveDaysThisMonth = approved.Sum(l => DaysInRangeOverlap(l.StartDate, l.EndDate, monthStart, monthEnd)),
                ApprovedLeaveDaysYearToDate = approved.Sum(l => DaysInRangeOverlap(l.StartDate, l.EndDate, yearStart, today)),
                PendingLeaveRequests = myLeaves.Count(l => l.Status == LeaveRequestStatus.Pending),
                NextApprovedLeaveStart = approved
                    .Where(l => l.EndDate >= today)
                    .OrderBy(l => l.StartDate)
                    .Select(l => (DateOnly?)l.StartDate)
                    .FirstOrDefault()
            };
        }

        var activeTasks = (await context.TaskItems.AsNoTracking()
            .Include(t => t.Rotation)
            .Where(t => isAdmin || (t.AssignedToUserId == userId
                        && (t.Status == TaskItemStatus.Pending || t.Status == TaskItemStatus.InProgress)))
            .ToListAsync())
            .OrderBy(t => t.DueDate == null ? 1 : 0)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.Title)
            .Take(20)
            .ToList();

        var notifications = (await context.Notifications.AsNoTracking()
            .Where(n => isAdmin || (n.RecipientUserId == userId && n.Status != NotificationStatus.Archived))
            .ToListAsync())
            .OrderBy(n => n.Status)
            .ThenByDescending(n => n.CreatedAtUtc)
            .Take(15)
            .ToList();

        var rotations = (await context.Rotations.AsNoTracking()
            .Where(r => isAdmin || r.Status == RotationStatus.Planned || r.Status == RotationStatus.Active)
            .ToListAsync())
            .OrderBy(r => r.Status)
            .ThenBy(r => r.StartAt)
            .Take(15)
            .ToList();

        var adminSummary = new AdminDashboardSummaryViewModel();
        var supervisorSummary = new SupervisorDashboardSummaryViewModel();
        IReadOnlyList<ManagedUserSummaryViewModel> managedUsers = Array.Empty<ManagedUserSummaryViewModel>();
        IReadOnlyList<AttendanceReportViewModel> attendanceReports = Array.Empty<AttendanceReportViewModel>();
        var model = new DashboardViewModel
        {
            IsAdmin = isAdmin,
            Attendance = attendance,
            AdminSummary = adminSummary,
            SupervisorSummary = supervisorSummary,
            ActiveTasks = activeTasks,
            Notifications = notifications,
            Rotations = rotations,
            ManagedUsers = managedUsers,
            AttendanceReports = attendanceReports
        };
        if (isAdmin)
        {
            adminSummary = new AdminDashboardSummaryViewModel
            {
                TotalUsers = await context.Users.CountAsync(),
                AdminUsers = (await userManager.GetUsersInRoleAsync(AppRoles.Admin)).Count,
                SupervisorUsers = (await userManager.GetUsersInRoleAsync(AppRoles.Manager)).Count,
                EmployeeUsers = (await userManager.GetUsersInRoleAsync(AppRoles.Employee)).Count,
                PendingLeaveRequests = await context.LeaveRequests.CountAsync(l => l.Status == LeaveRequestStatus.Pending),
                OpenTasks = await context.TaskItems.CountAsync(t => t.Status == TaskItemStatus.Pending || t.Status == TaskItemStatus.InProgress),
                ActiveRotations = await context.Rotations.CountAsync(r => r.Status == RotationStatus.Planned || r.Status == RotationStatus.Active),
                UnreadNotifications = await context.Notifications.CountAsync(n => n.Status == NotificationStatus.Unread)
            };

            model.AdminSummary = adminSummary;
        }
        else if (User.IsInRole(AppRoles.Manager))
        {
            var supervisorUsers = await userManager.GetUsersInRoleAsync(AppRoles.Manager);
            var employeeUsers = await userManager.GetUsersInRoleAsync(AppRoles.Employee);
            var nonAdminUsers = supervisorUsers
                .Concat(employeeUsers)
                .GroupBy(u => u.Id)
                .Select(g => g.First())
                .OrderBy(u => u.Email)
                .ToList();

            var userRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var user in nonAdminUsers)
            {
                var roles = await userManager.GetRolesAsync(user);
                var role = roles.Contains(AppRoles.Manager) ? AppRoles.Manager : AppRoles.Employee;
                userRoles[user.Id] = role;
            }

            var approvedLeaveRows = await context.LeaveRequests.AsNoTracking()
                .Where(l => l.Status == LeaveRequestStatus.Approved)
                .ToListAsync();
            var pendingCounts = await context.LeaveRequests.AsNoTracking()
                .Where(l => nonAdminUsers.Select(u => u.Id).Contains(l.EmployeeUserId) && l.Status == LeaveRequestStatus.Pending)
                .GroupBy(l => l.EmployeeUserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToListAsync();
            var pendingCountByUserId = pendingCounts.ToDictionary(x => x.UserId, x => x.Count, StringComparer.OrdinalIgnoreCase);

            var displayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var user in nonAdminUsers)
            {
                displayNames[user.Id] = await GetDisplayNameAsync(user);
            }

            managedUsers = nonAdminUsers
                .Select(u => new ManagedUserSummaryViewModel
                {
                    UserId = u.Id,
                    DisplayName = displayNames.TryGetValue(u.Id, out var displayName) ? displayName : u.Id,
                    Role = userRoles.TryGetValue(u.Id, out var role) ? role : AppRoles.Employee
                })
                .ToList();

            attendanceReports = nonAdminUsers
                .Select(u =>
                {
                    var userLeaves = approvedLeaveRows.Where(l => l.EmployeeUserId == u.Id).ToList();

                    return new AttendanceReportViewModel
                    {
                        UserId = u.Id,
                        DisplayName = displayNames.TryGetValue(u.Id, out var displayName) ? displayName : u.Id,
                        ApprovedLeaveDaysThisMonth = userLeaves.Sum(l => DaysInRangeOverlap(l.StartDate, l.EndDate, monthStart, monthEnd)),
                        PendingLeaveRequests = pendingCountByUserId.TryGetValue(u.Id, out var pending) ? pending : 0
                    };
                })
                .OrderByDescending(r => r.PendingLeaveRequests)
                .ThenBy(r => r.DisplayName)
                .ToList();

            var usersRouteValues = new Dictionary<string, object?>
            {
                ["reportsPage"] = reportsPage
            };
            var reportsRouteValues = new Dictionary<string, object?>
            {
                ["usersPage"] = usersPage
            };

            supervisorSummary = new SupervisorDashboardSummaryViewModel
            {
                PendingLeaveRequests = await context.LeaveRequests.CountAsync(l => l.Status == LeaveRequestStatus.Pending),
                OpenTasks = await context.TaskItems.CountAsync(t => t.Status == TaskItemStatus.Pending || t.Status == TaskItemStatus.InProgress),
                ActiveRotations = await context.Rotations.CountAsync(r => r.Status == RotationStatus.Planned || r.Status == RotationStatus.Active),
                ManagedUsers = nonAdminUsers.Count,
                AttendanceReports = attendanceReports.Count
            };

            model.ManagedUsersPage = PaginationHelper.Create(
                managedUsers,
                usersPage,
                6,
                "Dashboard",
                nameof(Index),
                "usersPage",
                usersRouteValues);

            model.AttendanceReportsPage = PaginationHelper.Create(
                attendanceReports,
                reportsPage,
                6,
                "Dashboard",
                nameof(Index),
                "reportsPage",
                reportsRouteValues);

            model.ManagedUsers = managedUsers;
            model.AttendanceReports = attendanceReports;
            model.SupervisorSummary = supervisorSummary;
        }

        if (isAdmin)
        {
            return View("Admin", model);
        }

        if (User.IsInRole(AppRoles.Manager))
        {
            return View("Supervisor", model);
        }

        return View("Employee", model);
    }

    private static int DaysInRangeOverlap(DateOnly leaveStart, DateOnly leaveEnd, DateOnly windowStart, DateOnly windowEnd)
    {
        var a = leaveStart > windowStart ? leaveStart : windowStart;
        var b = leaveEnd < windowEnd ? leaveEnd : windowEnd;
        if (a > b) return 0;
        return b.DayNumber - a.DayNumber + 1;
    }

    private async Task<string> GetDisplayNameAsync(IdentityUser user)
    {
        var profile = await context.EmployeeProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == user.Id);
        return EmployeeDirectoryService.GetDisplayName(user, profile);
    }
}
