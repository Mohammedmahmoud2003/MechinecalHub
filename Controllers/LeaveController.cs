using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HRSystem.Data;
using HRSystem.Infrastructure;
using HRSystem.Models;

namespace HRSystem.Controllers;

[Authorize]
public class LeaveController(
    ApplicationDbContext context,
    UserManager<IdentityUser> userManager,
    ChangeLogService changeLogService) : Controller
{
    private readonly ChangeLogService _changeLogService = changeLogService;

    public async Task<IActionResult> Index(int page = 1)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var canReview = CanReviewLeaveRequests();

        var query = context.LeaveRequests.AsNoTracking();
        if (!canReview)
        {
            query = query.Where(l => l.EmployeeUserId == userId);
        }

        var items = (await query
            .ToListAsync())
            .OrderByDescending(l => l.SubmittedAtUtc)
            .ToList();

        await PopulateUserLookupAsync(items);
        ViewBag.CanReviewLeaveRequests = canReview;
        return View(new LeaveIndexViewModel
        {
            Requests = PaginationHelper.Create(items, page, 10, "Leave", nameof(Index)),
            PendingCount = items.Count(x => x.Status == LeaveRequestStatus.Pending),
            ApprovedCount = items.Count(x => x.Status == LeaveRequestStatus.Approved),
            DraftCount = items.Count(x => x.Status == LeaveRequestStatus.Draft)
        });
    }

    /// <summary>Pending requests from other employees that the current user may approve or reject.</summary>
    public async Task<IActionResult> Pending(int page = 1)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        if (!CanReviewLeaveRequests()) return Forbid();

        var queue = (await context.LeaveRequests.AsNoTracking()
            .Where(l => l.Status == LeaveRequestStatus.Pending && l.EmployeeUserId != userId)
            .ToListAsync())
            .OrderBy(l => l.SubmittedAtUtc)
            .ToList();

        await PopulateUserLookupAsync(queue);

        return View(new LeavePendingViewModel
        {
            Requests = PaginationHelper.Create(queue, page, 8, "Leave", nameof(Pending))
        });
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var leaveRequest = await context.LeaveRequests.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (leaveRequest == null) return NotFound();

        await SetLeaveUserLookupsAsync(leaveRequest);

        return View(leaveRequest);
    }

    public IActionResult Create()
    {
        return View(new LeaveRequest
        {
            StartDate = DateOnly.FromDateTime(DateTime.Today),
            EndDate = DateOnly.FromDateTime(DateTime.Today)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("StartDate,EndDate,Reason")] LeaveRequest leaveRequest)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        if (ModelState.IsValid)
        {
            leaveRequest.EmployeeUserId = userId;
            leaveRequest.Status = LeaveRequestStatus.Pending;
            leaveRequest.SubmittedAtUtc = DateTimeOffset.UtcNow;
            context.LeaveRequests.Add(leaveRequest);
            await context.SaveChangesAsync();
            await _changeLogService.LogAsync(
                context,
                "Leave",
                "Created",
                $"Created leave request for {leaveRequest.StartDate:yyyy-MM-dd} to {leaveRequest.EndDate:yyyy-MM-dd}",
                $"EmployeeUserId: {userId}",
                userId);
            TempData["Message"] = "Leave request submitted.";
            return RedirectToAction(nameof(Index));
        }

        return View(leaveRequest);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var leaveRequest = await context.LeaveRequests.FindAsync(id);
        if (leaveRequest == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (leaveRequest.EmployeeUserId != userId) return Forbid();
        if (leaveRequest.Status is not (LeaveRequestStatus.Draft or LeaveRequestStatus.Pending))
        {
            TempData["Error"] = "Only draft or pending requests can be edited.";
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(leaveRequest);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,StartDate,EndDate,Reason")] LeaveRequest model)
    {
        if (id != model.Id) return NotFound();

        var leaveRequest = await context.LeaveRequests.FindAsync(id);
        if (leaveRequest == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (leaveRequest.EmployeeUserId != userId) return Forbid();
        if (leaveRequest.Status is not (LeaveRequestStatus.Draft or LeaveRequestStatus.Pending))
        {
            TempData["Error"] = "Only draft or pending requests can be edited.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (ModelState.IsValid)
        {
            try
            {
                leaveRequest.StartDate = model.StartDate;
                leaveRequest.EndDate = model.EndDate;
                leaveRequest.Reason = model.Reason;
                await context.SaveChangesAsync();
                await _changeLogService.LogAsync(
                    context,
                    "Leave",
                    "Updated",
                    $"Updated leave request #{leaveRequest.Id}",
                    $"EmployeeUserId: {userId}; {model.StartDate:yyyy-MM-dd} to {model.EndDate:yyyy-MM-dd}",
                    userId);
                TempData["Message"] = "Leave request updated.";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await LeaveRequestExists(model.Id)) return NotFound();
                throw;
            }

            return RedirectToAction(nameof(Index));
        }

        return View(model);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var leaveRequest = await context.LeaveRequests.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (leaveRequest == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (leaveRequest.EmployeeUserId != userId) return Forbid();
        if (leaveRequest.Status is not (LeaveRequestStatus.Draft or LeaveRequestStatus.Pending))
        {
            TempData["Error"] = "Only draft or pending requests can be deleted.";
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(leaveRequest);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var leaveRequest = await context.LeaveRequests.FindAsync(id);
        if (leaveRequest == null) return RedirectToAction(nameof(Index));

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (leaveRequest.EmployeeUserId != userId) return Forbid();
        if (leaveRequest.Status is not (LeaveRequestStatus.Draft or LeaveRequestStatus.Pending))
        {
            TempData["Error"] = "Only draft or pending requests can be deleted.";
            return RedirectToAction(nameof(Details), new { id });
        }

        context.LeaveRequests.Remove(leaveRequest);
        await context.SaveChangesAsync();
        await _changeLogService.LogAsync(
            context,
            "Leave",
            "Deleted",
            $"Deleted leave request #{leaveRequest.Id}",
            $"EmployeeUserId: {userId}",
            userId);
        TempData["Message"] = "Leave request removed.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, string? returnTo = null)
    {
        var leaveRequest = await context.LeaveRequests.FindAsync(id);
        if (leaveRequest == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        if (!CanReviewLeaveRequests()) return Forbid();

        if (leaveRequest.Status != LeaveRequestStatus.Pending)
        {
            TempData["Error"] = "Only pending requests can be approved.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (string.Equals(leaveRequest.EmployeeUserId, userId, StringComparison.Ordinal))
        {
            TempData["Error"] = "You cannot approve your own leave request.";
            return RedirectToAction(nameof(Details), new { id });
        }

        leaveRequest.Status = LeaveRequestStatus.Approved;
        leaveRequest.ReviewerUserId = userId;
        leaveRequest.ReviewedAtUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();
        await EmployeeDirectoryService.RecalculateLeaveImpactAsync(context, leaveRequest.EmployeeUserId);
        await context.SaveChangesAsync();
        await _changeLogService.LogAsync(
            context,
            "Leave",
            "Approved",
            $"Approved leave request #{leaveRequest.Id}",
            $"EmployeeUserId: {leaveRequest.EmployeeUserId}; {leaveRequest.StartDate:yyyy-MM-dd} to {leaveRequest.EndDate:yyyy-MM-dd}",
            userId);
        TempData["Message"] = "Leave request approved.";
        return RedirectAfterReview(returnTo, id);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? returnTo = null)
    {
        var leaveRequest = await context.LeaveRequests.FindAsync(id);
        if (leaveRequest == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        if (!CanReviewLeaveRequests()) return Forbid();

        if (leaveRequest.Status != LeaveRequestStatus.Pending)
        {
            TempData["Error"] = "Only pending requests can be rejected.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (string.Equals(leaveRequest.EmployeeUserId, userId, StringComparison.Ordinal))
        {
            TempData["Error"] = "You cannot reject your own leave request.";
            return RedirectToAction(nameof(Details), new { id });
        }

        leaveRequest.Status = LeaveRequestStatus.Rejected;
        leaveRequest.ReviewerUserId = userId;
        leaveRequest.ReviewedAtUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();
        await EmployeeDirectoryService.RecalculateLeaveImpactAsync(context, leaveRequest.EmployeeUserId);
        await context.SaveChangesAsync();
        await _changeLogService.LogAsync(
            context,
            "Leave",
            "Rejected",
            $"Rejected leave request #{leaveRequest.Id}",
            $"EmployeeUserId: {leaveRequest.EmployeeUserId}; {leaveRequest.StartDate:yyyy-MM-dd} to {leaveRequest.EndDate:yyyy-MM-dd}",
            userId);
        TempData["Message"] = "Leave request rejected.";
        return RedirectAfterReview(returnTo, id);
    }

    private IActionResult RedirectAfterReview(string? returnTo, int id) =>
        string.Equals(returnTo, "queue", StringComparison.OrdinalIgnoreCase)
            ? RedirectToAction(nameof(Pending))
            : RedirectToAction(nameof(Details), new { id });

    private async Task PopulateUserLookupAsync(IEnumerable<LeaveRequest> items)
    {
        var ids = items
            .SelectMany(l => new[] { l.EmployeeUserId, l.ReviewerUserId })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            ViewBag.UserNamesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var users = await userManager.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, u.UserName })
            .ToListAsync();

        var profiles = await context.EmployeeProfiles.AsNoTracking()
            .Where(p => ids.Contains(p.UserId))
            .ToListAsync();
        var profileByUserId = profiles.ToDictionary(p => p.UserId, StringComparer.OrdinalIgnoreCase);

        ViewBag.UserNamesById = users.ToDictionary(
            u => u.Id,
            u => profileByUserId.TryGetValue(u.Id, out var profile)
                ? EmployeeDirectoryService.GetDisplayName(new IdentityUser { Id = u.Id, Email = u.Email, UserName = u.UserName }, profile)
                : (string.IsNullOrWhiteSpace(u.Email) ? u.UserName ?? u.Id : u.Email),
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task SetLeaveUserLookupsAsync(LeaveRequest item)
    {
        await PopulateUserLookupAsync(new[] { item });
    }

    private bool CanReviewLeaveRequests() => User.IsInRole(AppRoles.Manager) || User.IsInRole(AppRoles.Admin);

    private Task<bool> LeaveRequestExists(int id) =>
        context.LeaveRequests.AnyAsync(e => e.Id == id);
}
