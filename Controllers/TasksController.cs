using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using HRSystem.Data;
using HRSystem.Infrastructure;
using HRSystem.Models;

namespace HRSystem.Controllers;

[Authorize]
public class TasksController(
    ApplicationDbContext context,
    UserManager<IdentityUser> userManager,
    ChangeLogService changeLogService) : Controller
{
    private readonly ChangeLogService _changeLogService = changeLogService;

    public async Task<IActionResult> Index(int page = 1)
    {
        if (!CanManageTasks())
        {
            TempData["Error"] = "Task Management is available for Admin and Supervisor users. Your assigned tasks are on Home.";
            return RedirectToAction("Index", "Dashboard");
        }

        var items = (await context.TaskItems
            .AsNoTracking()
            .Include(t => t.Rotation)
            .ToListAsync())
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToList();

        await PopulateAssignedUsersLookupAsync(items.Select(t => t.AssignedToUserId));
        return View(new TasksIndexViewModel
        {
            Tasks = PaginationHelper.Create(items, page, 10, "Tasks", nameof(Index)),
            TotalCount = items.Count,
            PendingCount = items.Count(t => t.Status == TaskItemStatus.Pending),
            InProgressCount = items.Count(t => t.Status == TaskItemStatus.InProgress),
            CompletedCount = items.Count(t => t.Status == TaskItemStatus.Completed)
        });
    }

    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    public async Task<IActionResult> Daily(int page = 1)
    {
        var templates = await context.DailyTaskTemplates
            .AsNoTracking()
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Title)
            .ToListAsync();

        return View(new DailyTaskTemplatesViewModel
        {
            Templates = templates
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    public async Task<IActionResult> LoadDailyTasks()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var templates = await context.DailyTaskTemplates.AsNoTracking()
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Title)
            .ToListAsync();

        var existingTitles = await context.TaskItems.AsNoTracking()
            .Where(task => task.DueDate == today)
            .Select(task => task.Title)
            .ToListAsync();

        var existingSet = new HashSet<string>(existingTitles, StringComparer.OrdinalIgnoreCase);
        var created = 0;

        foreach (var template in templates)
        {
            if (existingSet.Contains(template.Title))
            {
                continue;
            }

            context.TaskItems.Add(new TaskItem
            {
                Title = template.Title,
                Description = template.Description,
                Status = TaskItemStatus.Pending,
                DueDate = today,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            created++;
        }

        if (created > 0)
        {
            await context.SaveChangesAsync();
            await _changeLogService.LogAsync(
                context,
                "Task",
                "LoadedDaily",
                "Loaded daily tasks onto the board",
                $"Created {created} tasks for {today:yyyy-MM-dd}");
            TempData["Message"] = created == 1
                ? "1 daily task was loaded onto the board."
                : $"{created} daily tasks were loaded onto the board.";
        }
        else
        {
            TempData["Message"] = "All daily tasks for today were already on the board.";
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var taskItem = await context.TaskItems
            .AsNoTracking()
            .Include(t => t.Rotation)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (taskItem == null) return NotFound();

        await SetAssignedUserNameAsync(taskItem.AssignedToUserId);

        return View(taskItem);
    }

    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    public async Task<IActionResult> Create()
    {
        await PopulateUsersDropDownAsync(DateOnly.FromDateTime(DateTime.Today));
        return View(new TaskItem { Status = TaskItemStatus.Pending });
    }

    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var taskItem = await context.TaskItems
            .AsNoTracking()
            .Include(t => t.Rotation)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (taskItem == null) return NotFound();

        await PopulateUsersDropDownAsync(taskItem.DueDate, taskItem.AssignedToUserId);
        await PopulateRotationsDropDownAsync(taskItem.RotationId);
        await SetAssignedUserNameAsync(taskItem.AssignedToUserId);

        return View(taskItem);
    }

    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    public async Task<IActionResult> CreateDaily()
    {
        return View(new DailyTaskTemplate
        {
            SortOrder = await GetNextTemplateSortOrderAsync()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Title,Description,Status,DueDate,AssignedToUserId")] TaskItem taskItem)
    {
        if (ModelState.IsValid)
        {
            taskItem.CreatedAtUtc = DateTimeOffset.UtcNow;
            context.TaskItems.Add(taskItem);
            await context.SaveChangesAsync();
            await _changeLogService.LogAsync(
                context,
                "Task",
                "Created",
                $"Created task {taskItem.Title}",
                $"AssignedTo: {taskItem.AssignedToUserId ?? "Unassigned"}");
            TempData["Message"] = "Task created.";
            return RedirectToAction(nameof(Index));
        }

        await PopulateUsersDropDownAsync(taskItem.DueDate ?? DateOnly.FromDateTime(DateTime.Today), taskItem.AssignedToUserId);
        return View(taskItem);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,Title,Description,Status,DueDate,AssignedToUserId,RotationId,CreatedAtUtc")] TaskItem input)
    {
        if (id != input.Id) return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                var taskItem = await context.TaskItems.FindAsync(id);
                if (taskItem == null) return NotFound();

                taskItem.Title = input.Title;
                taskItem.Description = input.Description;
                taskItem.Status = input.Status;
                taskItem.DueDate = input.DueDate;
                taskItem.AssignedToUserId = input.AssignedToUserId;
                taskItem.RotationId = input.RotationId;
                await context.SaveChangesAsync();
                await _changeLogService.LogAsync(
                    context,
                    "Task",
                    "Updated",
                    $"Updated task {taskItem.Title}",
                    $"TaskId: {taskItem.Id}; AssignedTo: {taskItem.AssignedToUserId ?? "Unassigned"}");
                TempData["Message"] = "Task updated.";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await TaskItemExists(input.Id)) return NotFound();
                throw;
            }

            return RedirectToAction(nameof(Index));
        }

        await PopulateUsersDropDownAsync(input.DueDate, input.AssignedToUserId);
        await PopulateRotationsDropDownAsync(input.RotationId);
        await SetAssignedUserNameAsync(input.AssignedToUserId);
        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDaily([Bind("Title,Description,SortOrder")] DailyTaskTemplate template)
    {
        if (ModelState.IsValid)
        {
            template.CreatedAtUtc = DateTimeOffset.UtcNow;
            context.DailyTaskTemplates.Add(template);
            await context.SaveChangesAsync();
            await _changeLogService.LogAsync(
                context,
                "DailyTaskTemplate",
                "Created",
                $"Created daily task template {template.Title}",
                $"SortOrder: {template.SortOrder}");
            TempData["Message"] = "Daily task template created.";
            return RedirectToAction(nameof(Daily));
        }

        template.SortOrder = await GetNextTemplateSortOrderAsync();
        return View(template);
    }

    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    public async Task<IActionResult> EditDaily(int? id)
    {
        if (id == null) return NotFound();

        var template = await context.DailyTaskTemplates.FindAsync(id);
        if (template == null) return NotFound();

        return View(template);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditDaily(int id, [Bind("Id,Title,Description,SortOrder")] DailyTaskTemplate input)
    {
        if (id != input.Id) return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                var template = await context.DailyTaskTemplates.FindAsync(id);
                if (template == null) return NotFound();

                template.Title = input.Title;
                template.Description = input.Description;
                template.SortOrder = input.SortOrder;
                template.UpdatedAtUtc = DateTimeOffset.UtcNow;
                await context.SaveChangesAsync();
                await _changeLogService.LogAsync(
                    context,
                    "DailyTaskTemplate",
                    "Updated",
                    $"Updated daily task template {template.Title}",
                    $"TemplateId: {template.Id}; SortOrder: {template.SortOrder}");
                TempData["Message"] = "Daily task template updated.";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await DailyTaskTemplateExists(input.Id)) return NotFound();
                throw;
            }

            return RedirectToAction(nameof(Daily));
        }

        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    public async Task<IActionResult> DeleteDaily(int id)
    {
        var template = await context.DailyTaskTemplates.FindAsync(id);
        if (template != null)
        {
            context.DailyTaskTemplates.Remove(template);
            await context.SaveChangesAsync();
            await _changeLogService.LogAsync(
                context,
                "DailyTaskTemplate",
                "Deleted",
                $"Deleted daily task template {template.Title}",
                $"TemplateId: {template.Id}");
            TempData["Message"] = "Daily task template deleted.";
        }

        return RedirectToAction(nameof(Daily));
    }

    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var taskItem = await context.TaskItems
            .AsNoTracking()
            .Include(t => t.Rotation)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (taskItem == null) return NotFound();

        await SetAssignedUserNameAsync(taskItem.AssignedToUserId);

        return View(taskItem);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    public async Task<IActionResult> Approve(int id)
    {
        var taskItem = await context.TaskItems.FindAsync(id);
        if (taskItem == null) return NotFound();

        if (taskItem.Status != TaskItemStatus.Pending)
        {
            TempData["Error"] = "Only pending tasks can be approved.";
            return RedirectToAction(nameof(Details), new { id });
        }

        taskItem.Status = TaskItemStatus.InProgress;
        await context.SaveChangesAsync();
        await _changeLogService.LogAsync(
            context,
            "Task",
            "Approved",
            $"Approved task #{taskItem.Id}",
            $"Status changed to InProgress");
        TempData["Message"] = "Task approved (moved to in progress).";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    public async Task<IActionResult> Reject(int id)
    {
        var taskItem = await context.TaskItems.FindAsync(id);
        if (taskItem == null) return NotFound();

        if (taskItem.Status != TaskItemStatus.Pending)
        {
            TempData["Error"] = "Only pending tasks can be rejected.";
            return RedirectToAction(nameof(Details), new { id });
        }

        taskItem.Status = TaskItemStatus.Cancelled;
        await context.SaveChangesAsync();
        await _changeLogService.LogAsync(
            context,
            "Task",
            "Rejected",
            $"Rejected task #{taskItem.Id}",
            $"Status changed to Cancelled");
        TempData["Message"] = "Task rejected (cancelled).";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id)
    {
        var taskItem = await context.TaskItems.FindAsync(id);
        if (taskItem == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        if (!string.Equals(taskItem.AssignedToUserId, userId, StringComparison.Ordinal) && !User.IsInRole(AppRoles.Admin))
        {
            return Forbid();
        }

        if (taskItem.Status != TaskItemStatus.InProgress)
        {
            TempData["Error"] = "Only in-progress tasks can be marked completed.";
            return RedirectToAction(nameof(Details), new { id });
        }

        taskItem.Status = TaskItemStatus.Completed;
        await context.SaveChangesAsync();
        await _changeLogService.LogAsync(
            context,
            "Task",
            "Completed",
            $"Completed task #{taskItem.Id}",
            $"Completed by user {userId}");
        TempData["Message"] = "Task marked as completed.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var taskItem = await context.TaskItems.FindAsync(id);
        if (taskItem != null)
        {
            context.TaskItems.Remove(taskItem);
            await context.SaveChangesAsync();
            await _changeLogService.LogAsync(
                context,
                "Task",
                "Deleted",
                $"Deleted task #{taskItem.Id}",
                $"Title: {taskItem.Title}");
            TempData["Message"] = "Task deleted.";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateRotationsDropDownAsync(int? selectedId = null)
    {
        var rotations = await context.Rotations.AsNoTracking()
            .OrderBy(r => r.Title)
            .Select(r => new { r.Id, r.Title })
            .ToListAsync();
        ViewBag.RotationId = new SelectList(rotations, "Id", "Title", selectedId);
    }

    private async Task PopulateUsersDropDownAsync(DateOnly? forDate = null, string? selectedUserId = null)
    {
        var blockedUserIds = forDate.HasValue
            ? await EmployeeDirectoryService.GetUnavailableUserIdsAsync(context, forDate.Value)
            : new List<string>();

        var adminUsers = await userManager.GetUsersInRoleAsync(AppRoles.Admin);
        var adminUserIds = adminUsers.Select(u => u.Id).ToList();

        var query = userManager.Users.AsNoTracking()
            .Where(u => !adminUserIds.Contains(u.Id));
        if (blockedUserIds.Count > 0)
        {
            query = query.Where(u => !blockedUserIds.Contains(u.Id) || u.Id == selectedUserId);
        }

        var users = await query
            .OrderBy(u => u.Email)
            .Select(u => new { u.Id, u.Email, u.UserName })
            .ToListAsync();

        var userIds = users.Select(u => u.Id).ToList();
        var profiles = await context.EmployeeProfiles.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .ToListAsync();
        var profileByUserId = profiles.ToDictionary(p => p.UserId, StringComparer.OrdinalIgnoreCase);

        var listItems = users.Select(u => new
        {
            u.Id,
            DisplayName = profileByUserId.TryGetValue(u.Id, out var profile)
                ? EmployeeDirectoryService.GetDisplayName(new IdentityUser { Id = u.Id, Email = u.Email, UserName = u.UserName }, profile)
                : (string.IsNullOrWhiteSpace(u.Email) ? u.UserName ?? u.Id : u.Email)
        }).ToList();

        ViewBag.AssignedToUserId = new SelectList(listItems, "Id", "DisplayName", selectedUserId);
    }

    private async Task PopulateAssignedUsersLookupAsync(IEnumerable<string?> userIds)
    {
        var ids = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (ids.Count == 0)
        {
            ViewBag.AssignedUsersById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

        ViewBag.AssignedUsersById = users.ToDictionary(
            u => u.Id,
            u => profileByUserId.TryGetValue(u.Id, out var profile)
                ? EmployeeDirectoryService.GetDisplayName(new IdentityUser { Id = u.Id, Email = u.Email, UserName = u.UserName }, profile)
                : (string.IsNullOrWhiteSpace(u.Email) ? u.UserName ?? u.Id : u.Email),
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<bool> DailyTaskTemplateExists(int id) => await context.DailyTaskTemplates.AnyAsync(e => e.Id == id);

    private bool CanManageTasks() => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Manager);

    private async Task<int> GetNextTemplateSortOrderAsync()
    {
        var max = await context.DailyTaskTemplates.MaxAsync(t => (int?)t.SortOrder);
        return (max ?? 0) + 1;
    }

    private async Task SetAssignedUserNameAsync(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            ViewBag.AssignedToUserName = null;
            return;
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            ViewBag.AssignedToUserName = userId;
            return;
        }

        var profile = await context.EmployeeProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
        ViewBag.AssignedToUserName = EmployeeDirectoryService.GetDisplayName(user, profile);
    }

    private Task<bool> TaskItemExists(int id) => context.TaskItems.AnyAsync(e => e.Id == id);
}
