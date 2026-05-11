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
public class RotationsController(
    ApplicationDbContext context,
    UserManager<IdentityUser> userManager,
    ChangeLogService changeLogService) : Controller
{
    private readonly ChangeLogService _changeLogService = changeLogService;

    public async Task<IActionResult> Index(int page = 1)
    {
        if (!CanManageRotations())
        {
            TempData["Error"] = "Rotation Management is available for Admin and Supervisor users. Your rotation preview is on Home.";
            return RedirectToAction("Index", "Dashboard");
        }

        var items = (await context.Rotations.AsNoTracking()
            .ToListAsync())
            .OrderByDescending(r => r.StartAt)
            .ToList();

        await PopulateOwnerLookupAsync(items);
        return View(new RotationsIndexViewModel
        {
            Rotations = PaginationHelper.Create(items, page, 10, "Rotations", nameof(Index)),
            PlannedCount = items.Count(r => r.Status == RotationStatus.Planned),
            ActiveCount = items.Count(r => r.Status == RotationStatus.Active),
            CompletedCount = items.Count(r => r.Status == RotationStatus.Completed)
        });
    }

    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    public async Task<IActionResult> Details(int? id, int schedulePage = 1)
    {
        if (id == null) return NotFound();

        var rotation = await context.Rotations
            .AsNoTracking()
            .Include(r => r.TaskItems)
            .Include(r => r.ScheduleEntries)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (rotation == null) return NotFound();

        await PopulateOwnerLookupAsync(new[] { rotation });
        var importedSchedule = rotation.ScheduleEntries
            .GroupBy(x => new { x.EmployeeCode, x.EmployeeName, x.JobTitle })
            .Select(group =>
            {
                var orderedDates = group.Select(x => x.WorkDate).OrderBy(x => x).ToList();
                var leaveCount = group.Count(x => EmployeeDirectoryService.IsLeaveStatus(x.Status));
                var workingCount = group.Count(x => !EmployeeDirectoryService.IsLeaveStatus(x.Status));

                return new RotationScheduleSummaryViewModel
                {
                    EmployeeCode = group.Key.EmployeeCode,
                    EmployeeName = group.Key.EmployeeName,
                    JobTitle = group.Key.JobTitle,
                    Days = group.Count(),
                    StartDate = orderedDates.FirstOrDefault(),
                    EndDate = orderedDates.LastOrDefault(),
                    LeaveCount = leaveCount,
                    WorkingCount = workingCount
                };
            })
            .OrderBy(x => x.EmployeeName)
            .ThenBy(x => x.EmployeeCode)
            .ToList();

        ViewBag.ImportedSchedulePage = PaginationHelper.Create(
            importedSchedule,
            schedulePage,
            8,
            "Rotations",
            nameof(Details),
            "schedulePage",
            new Dictionary<string, object?> { ["id"] = rotation.Id });

        return View(rotation);
    }

    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    public async Task<IActionResult> DownloadWorkbook(int? id)
    {
        if (id == null) return NotFound();

        var rotation = await context.Rotations
            .AsNoTracking()
            .Include(r => r.ScheduleEntries)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (rotation == null)
        {
            return NotFound();
        }

        var sheetName = string.IsNullOrWhiteSpace(rotation.SourceSheetName) ? "Sheet1" : rotation.SourceSheetName;
        var bytes = RotationWorkbookExporter.BuildWorkbook(rotation, rotation.ScheduleEntries.ToList(), sheetName);
        var fileName = BuildSafeFileName($"{rotation.Title}.xlsx");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    public IActionResult Create()
    {
        return View(new Rotation
        {
            StartAt = DateTimeOffset.Now,
            EndAt = DateTimeOffset.Now.AddDays(7),
            Status = RotationStatus.Planned
        });
    }

    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Title,Description,StartAt,EndAt,Status")] Rotation rotation, IFormFile? workbook)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (workbook is null || workbook.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Upload the Excel workbook before creating a rotation.");
        }

        if (ModelState.IsValid)
        {
            var now = DateTimeOffset.UtcNow;
            if (!string.Equals(Path.GetExtension(workbook!.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(string.Empty, "Upload an Excel workbook with the .xlsx extension.");
                return View(rotation);
            }

            try
            {
                using var uploadedStream = workbook.OpenReadStream();
                using var workbookBuffer = new MemoryStream();
                await uploadedStream.CopyToAsync(workbookBuffer);

                workbookBuffer.Position = 0;
                var parsedRotation = RotationWorkbookImporter.Read(workbookBuffer);
                var userIdsByCode = await GetEmployeeUserIdsByCodeAsync(parsedRotation.Employees.Select(employee => employee.Code));
                var skippedEntries = 0;

                rotation.Title = string.IsNullOrWhiteSpace(rotation.Title)
                    ? parsedRotation.RotationTitle
                    : rotation.Title.Trim();
                rotation.Description = string.IsNullOrWhiteSpace(rotation.Description)
                    ? $"Imported from {workbook.FileName} ({parsedRotation.StartDate:yyyy-MM-dd} to {parsedRotation.EndDate:yyyy-MM-dd})"
                    : rotation.Description.Trim();
                rotation.StartAt = new DateTimeOffset(parsedRotation.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                rotation.EndAt = new DateTimeOffset(parsedRotation.EndDate.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero);
                rotation.ImportedAtUtc = now;
                rotation.SourceWorkbookTitle = workbook.FileName;
                rotation.SourceSheetName = parsedRotation.SheetName;
                rotation.OwnerUserId = userId;
                rotation.CreatedAtUtc = now;

                foreach (var entry in parsedRotation.DayEntries)
                {
                    if (!userIdsByCode.TryGetValue(entry.Code, out var employeeUserId))
                    {
                        skippedEntries++;
                        continue;
                    }

                    rotation.ScheduleEntries.Add(new RotationScheduleEntry
                    {
                        EmployeeUserId = employeeUserId,
                        EmployeeCode = entry.Code,
                        EmployeeName = entry.Name,
                        JobTitle = entry.JobTitle,
                        WorkDate = entry.WorkDate,
                        OriginalStatus = entry.Status,
                        Status = entry.Status
                    });
                }

                context.Rotations.Add(rotation);
                await context.SaveChangesAsync();

                if (skippedEntries > 0)
                {
                    TempData["Message"] = $"Rotation created from workbook. {skippedEntries} workbook rows were skipped because no matching employee account was found.";
                }
                else
                {
                    TempData["Message"] = "Rotation created from workbook.";
                }

                await _changeLogService.LogAsync(
                    context,
                    "Rotation",
                    "Created",
                    $"Created rotation {rotation.Title} from workbook",
                    $"Sheet: {parsedRotation.SheetName}; Entries: {rotation.ScheduleEntries.Count}; Owner: {userId}",
                    userId);

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Unable to read the workbook: {ex.Message}");
                return View(rotation);
            }
        }

        return View(rotation);
    }

    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var rotation = await context.Rotations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);
        if (rotation == null) return NotFound();

        return View(rotation);
    }

    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,Title,Description,StartAt,EndAt,Status")] Rotation input, IFormFile? workbook)
    {
        if (id != input.Id) return NotFound();

        var rotation = await context.Rotations
            .Include(r => r.ScheduleEntries)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (rotation == null) return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                rotation.Title = input.Title;
                rotation.Description = input.Description;
                rotation.StartAt = input.StartAt;
                rotation.EndAt = input.EndAt;
                rotation.Status = input.Status;

                if (workbook is { Length: > 0 })
                {
                    if (!string.Equals(Path.GetExtension(workbook.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
                    {
                        ModelState.AddModelError(string.Empty, "Upload an Excel workbook with the .xlsx extension.");
                        input.SourceWorkbookTitle = rotation.SourceWorkbookTitle;
                        input.SourceSheetName = rotation.SourceSheetName;
                        return View(input);
                    }

                    using var uploadedStream = workbook.OpenReadStream();
                    using var workbookBuffer = new MemoryStream();
                    await uploadedStream.CopyToAsync(workbookBuffer);

                    workbookBuffer.Position = 0;
                    var parsedRotation = RotationWorkbookImporter.Read(workbookBuffer);
                    var userIdsByCode = await GetEmployeeUserIdsByCodeAsync(parsedRotation.Employees.Select(employee => employee.Code));

                    if (rotation.ScheduleEntries.Count > 0)
                    {
                        context.RotationScheduleEntries.RemoveRange(rotation.ScheduleEntries);
                        rotation.ScheduleEntries.Clear();
                    }

                    rotation.Title = string.IsNullOrWhiteSpace(rotation.Title)
                        ? parsedRotation.RotationTitle
                        : rotation.Title.Trim();
                    rotation.Description = string.IsNullOrWhiteSpace(rotation.Description)
                        ? $"Imported from {workbook.FileName} ({parsedRotation.StartDate:yyyy-MM-dd} to {parsedRotation.EndDate:yyyy-MM-dd})"
                        : rotation.Description.Trim();
                    rotation.StartAt = new DateTimeOffset(parsedRotation.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                    rotation.EndAt = new DateTimeOffset(parsedRotation.EndDate.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero);
                    rotation.ImportedAtUtc = DateTimeOffset.UtcNow;
                    rotation.SourceWorkbookTitle = workbook.FileName;
                    rotation.SourceSheetName = parsedRotation.SheetName;

                    foreach (var entry in parsedRotation.DayEntries)
                    {
                        if (!userIdsByCode.TryGetValue(entry.Code, out var employeeUserId))
                        {
                            continue;
                        }

                        rotation.ScheduleEntries.Add(new RotationScheduleEntry
                        {
                            EmployeeUserId = employeeUserId,
                            EmployeeCode = entry.Code,
                            EmployeeName = entry.Name,
                            JobTitle = entry.JobTitle,
                            WorkDate = entry.WorkDate,
                            OriginalStatus = entry.Status,
                            Status = entry.Status
                        });
                    }
                }

                await context.SaveChangesAsync();
                await _changeLogService.LogAsync(
                    context,
                    "Rotation",
                    "Updated",
                    $"Updated rotation {rotation.Title}",
                    $"RotationId: {rotation.Id}; Status: {rotation.Status}",
                    User.FindFirstValue(ClaimTypes.NameIdentifier));
                TempData["Message"] = "Rotation updated.";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await RotationExists(input.Id)) return NotFound();
                throw;
            }

            return RedirectToAction(nameof(Index));
        }

        return View(input);
    }

    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var rotation = await context.Rotations
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id);
        if (rotation == null) return NotFound();

        return View(rotation);
    }

    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var rotation = await context.Rotations.FindAsync(id);
        if (rotation == null) return NotFound();

        if (rotation.Status != RotationStatus.Planned)
        {
            TempData["Error"] = "Only planned rotations can be approved.";
            return RedirectToAction(nameof(Details), new { id });
        }

        rotation.Status = RotationStatus.Active;
        await context.SaveChangesAsync();
        await _changeLogService.LogAsync(
            context,
            "Rotation",
            "Approved",
            $"Approved rotation {rotation.Title}",
            $"RotationId: {rotation.Id}",
            User.FindFirstValue(ClaimTypes.NameIdentifier));
        TempData["Message"] = "Rotation approved (now active).";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id)
    {
        var rotation = await context.Rotations.FindAsync(id);
        if (rotation == null) return NotFound();

        if (rotation.Status is not (RotationStatus.Planned or RotationStatus.Active))
        {
            TempData["Error"] = "Only planned or active rotations can be rejected.";
            return RedirectToAction(nameof(Details), new { id });
        }

        rotation.Status = RotationStatus.Cancelled;
        await context.SaveChangesAsync();
        await _changeLogService.LogAsync(
            context,
            "Rotation",
            "Rejected",
            $"Rejected rotation {rotation.Title}",
            $"RotationId: {rotation.Id}",
            User.FindFirstValue(ClaimTypes.NameIdentifier));
        TempData["Message"] = "Rotation rejected (cancelled).";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var rotation = await context.Rotations
            .Include(r => r.TaskItems)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (rotation != null)
        {
            foreach (var task in rotation.TaskItems) task.RotationId = null;
            context.Rotations.Remove(rotation);
            await context.SaveChangesAsync();
            await _changeLogService.LogAsync(
                context,
                "Rotation",
                "Deleted",
                $"Deleted rotation {rotation.Title}",
                $"RotationId: {rotation.Id}",
                User.FindFirstValue(ClaimTypes.NameIdentifier));
            TempData["Message"] = "Rotation deleted.";
        }

        return RedirectToAction(nameof(Index));
    }

    private Task<bool> RotationExists(int id) => context.Rotations.AnyAsync(e => e.Id == id);

    private bool CanManageRotations() => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Manager);

    private static string BuildSafeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safe = new string(fileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "rotation.xlsx" : safe;
    }

    private async Task PopulateOwnerLookupAsync(IEnumerable<Rotation> rotations)
    {
        var ids = rotations
            .Select(r => r.OwnerUserId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            ViewBag.OwnerNamesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

        ViewBag.OwnerNamesById = users.ToDictionary(
            u => u.Id,
            u => profileByUserId.TryGetValue(u.Id, out var profile)
                ? EmployeeDirectoryService.GetDisplayName(new IdentityUser { Id = u.Id, Email = u.Email, UserName = u.UserName }, profile)
                : (string.IsNullOrWhiteSpace(u.Email) ? u.UserName ?? u.Id : u.Email),
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, string>> GetEmployeeUserIdsByCodeAsync(IEnumerable<string> codes)
    {
        var normalizedCodes = codes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedCodes.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var profiles = await context.EmployeeProfiles.AsNoTracking()
            .Where(profile => normalizedCodes.Contains(profile.EmployeeCode))
            .ToListAsync();

        return profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.EmployeeCode))
            .GroupBy(profile => profile.EmployeeCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().UserId, StringComparer.OrdinalIgnoreCase);
    }
}
