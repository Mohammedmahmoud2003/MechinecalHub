using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using HRSystem.Data;
using HRSystem.Models;

namespace HRSystem.Infrastructure;

public static class EmployeeDirectoryService
{
    public const string LeaveStatusLabel = "إجازة";

    public static async Task<List<string>> GetUnavailableUserIdsAsync(ApplicationDbContext context, DateOnly onDate)
    {
        var approvedLeaveUserIds = await context.LeaveRequests.AsNoTracking()
            .Where(l => l.Status == LeaveRequestStatus.Approved
                        && l.StartDate <= onDate
                        && l.EndDate >= onDate)
            .Select(l => l.EmployeeUserId)
            .Distinct()
            .ToListAsync();

        return approvedLeaveUserIds;
    }

    public static async Task RecalculateLeaveImpactAsync(ApplicationDbContext context, string userId)
    {
        var scheduleEntries = await context.RotationScheduleEntries
            .Where(entry => entry.EmployeeUserId == userId)
            .ToListAsync();

        if (scheduleEntries.Count == 0)
        {
            return;
        }

        var approvedLeaves = await context.LeaveRequests.AsNoTracking()
            .Where(l => l.EmployeeUserId == userId && l.Status == LeaveRequestStatus.Approved)
            .Select(l => new { l.StartDate, l.EndDate })
            .ToListAsync();

        foreach (var entry in scheduleEntries)
        {
            var onLeave = approvedLeaves.Any(l => l.StartDate <= entry.WorkDate && l.EndDate >= entry.WorkDate);
            entry.Status = onLeave ? LeaveStatusLabel : entry.OriginalStatus;
        }
    }

    public static bool IsLeaveStatus(string? status) =>
        string.Equals(status, LeaveStatusLabel, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "On Leave", StringComparison.OrdinalIgnoreCase);

    public static async Task RecalculateLeaveImpactAsync(ApplicationDbContext context, IEnumerable<string> userIds)
    {
        foreach (var userId in userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await RecalculateLeaveImpactAsync(context, userId);
        }
    }

    public static string GetDisplayName(IdentityUser user, EmployeeProfile? profile = null)
    {
        if (profile is not null && !string.IsNullOrWhiteSpace(profile.ArabicName))
        {
            return profile.ArabicName;
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            return user.Email;
        }

        if (!string.IsNullOrWhiteSpace(user.UserName))
        {
            return user.UserName;
        }

        return profile?.EmployeeCode ?? user.Id;
    }
}
