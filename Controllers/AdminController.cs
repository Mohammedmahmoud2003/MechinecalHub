using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HRSystem.Data;
using HRSystem.Infrastructure;
using HRSystem.Models;

namespace HRSystem.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class AdminController(
    ApplicationDbContext context,
    UserManager<IdentityUser> userManager,
    RoleManager<IdentityRole> roleManager,
    ChangeLogService changeLogService) : Controller
{
    private readonly ApplicationDbContext _context = context;
    private readonly UserManager<IdentityUser> _userManager = userManager;
    private readonly RoleManager<IdentityRole> _roleManager = roleManager;
    private readonly ChangeLogService _changeLogService = changeLogService;

    public async Task<IActionResult> Users(int page = 1)
    {
        var users = await _userManager.Users
            .OrderBy(user => user.Email)
            .ToListAsync();

        var userIds = users.Select(user => user.Id).ToList();
        var profiles = await _context.EmployeeProfiles.AsNoTracking()
            .Where(profile => userIds.Contains(profile.UserId))
            .ToListAsync();

        var profileByUserId = profiles.ToDictionary(profile => profile.UserId, StringComparer.OrdinalIgnoreCase);
        var currentUser = await _userManager.GetUserAsync(User);

        var rows = new List<AdminUserRowViewModel>(users.Count);
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var currentRole = roles.FirstOrDefault() ?? AppRoles.Employee;
            profileByUserId.TryGetValue(user.Id, out var profile);

            rows.Add(new AdminUserRowViewModel
            {
                Id = user.Id,
                Email = user.Email ?? user.UserName ?? "(no email)",
                UserName = user.UserName ?? user.Email ?? "(no username)",
                ArabicName = profile?.ArabicName ?? string.Empty,
                EmployeeCode = profile?.EmployeeCode ?? string.Empty,
                JobTitle = profile?.JobTitle ?? string.Empty,
                CurrentRole = currentRole,
                CurrentRoleLabel = GetRoleLabel(currentRole),
                IsCurrentUser = currentUser?.Id == user.Id
            });
        }

        return View(new AdminUsersViewModel
        {
            Users = PaginationHelper.Create(rows, page, 10, "Admin", nameof(Users)),
            RoleOptions = GetRoleOptions(),
            StatusMessage = TempData["AdminStatus"] as string
        });
    }

    public async Task<IActionResult> Edit(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return NotFound();
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        return View(await BuildEditViewModelAsync(user));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AdminUserEditViewModel model)
    {
        var user = await _userManager.FindByIdAsync(model.Id);
        if (user is null)
        {
            TempData["AdminStatus"] = "The selected user could not be found.";
            return RedirectToAction(nameof(Users));
        }

        var normalizedEmail = model.Email?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            ModelState.AddModelError(nameof(model.Email), "Email is required.");
        }
        else if (!string.Equals(user.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))
        {
            var existingUser = await _userManager.FindByEmailAsync(normalizedEmail);
            if (existingUser is not null && existingUser.Id != user.Id)
            {
                ModelState.AddModelError(nameof(model.Email), "That email is already in use.");
            }
        }

        var normalizedCode = model.EmployeeCode?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedCode))
        {
            var codeExists = await _context.EmployeeProfiles
                .AnyAsync(p => p.EmployeeCode == normalizedCode && p.UserId != user.Id);
            if (codeExists)
            {
                ModelState.AddModelError(nameof(model.EmployeeCode), "That employee code is already in use.");
            }
        }

        if (!ModelState.IsValid)
        {
            return View(await BuildEditViewModelAsync(user, model));
        }

        var profile = await _context.EmployeeProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
        if (profile is null)
        {
            profile = new EmployeeProfile
            {
                UserId = user.Id
            };
            _context.EmployeeProfiles.Add(profile);
        }

        user.Email = normalizedEmail;
        user.UserName = normalizedEmail;
        user.EmailConfirmed = true;

        profile.ArabicName = model.ArabicName?.Trim() ?? string.Empty;
        profile.EmployeeCode = normalizedCode;
        profile.JobTitle = model.JobTitle?.Trim() ?? string.Empty;
        profile.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(model.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var passwordResetResult = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
            if (!passwordResetResult.Succeeded)
            {
                var error = passwordResetResult.Errors.FirstOrDefault()?.Description ?? "Unable to reset the password.";
                ModelState.AddModelError(nameof(model.NewPassword), error);
                return View(await BuildEditViewModelAsync(user, model));
            }
        }

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            var error = updateResult.Errors.FirstOrDefault()?.Description ?? "Unable to save the account changes.";
            ModelState.AddModelError(string.Empty, error);
            return View(await BuildEditViewModelAsync(user, model));
        }

        await _context.SaveChangesAsync();
        await _changeLogService.LogAsync(
            _context,
            "Employee",
            "Updated",
            $"Updated account for {GetUserDisplayName(user, profile)}",
            $"Email: {normalizedEmail}; Code: {profile.EmployeeCode}; Job: {profile.JobTitle}",
            _userManager.GetUserId(User));

        TempData["AdminStatus"] = $"Updated {GetUserDisplayName(user, profile)}.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            TempData["AdminStatus"] = "Choose a user before deleting.";
            return RedirectToAction(nameof(Users));
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            TempData["AdminStatus"] = "The selected user could not be found.";
            return RedirectToAction(nameof(Users));
        }

        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.Id == user.Id)
        {
            TempData["AdminStatus"] = "You cannot delete your own account from this page.";
            return RedirectToAction(nameof(Users));
        }

        if (await IsLastAdminAsync(user))
        {
            TempData["AdminStatus"] = "You cannot delete the last admin account.";
            return RedirectToAction(nameof(Users));
        }

        var deleteResult = await _userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded)
        {
            var error = deleteResult.Errors.FirstOrDefault()?.Description ?? "Unable to delete the account.";
            TempData["AdminStatus"] = error;
            return RedirectToAction(nameof(Users));
        }

        var profile = await _context.EmployeeProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id);
        await _changeLogService.LogAsync(
            _context,
            "Employee",
            "Deleted",
            $"Deleted account for {GetUserDisplayName(user, profile)}",
            $"UserId: {user.Id}",
            _userManager.GetUserId(User));
        TempData["AdminStatus"] = $"Deleted {GetUserDisplayName(user, profile)}.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRole(string userId, string role)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(role))
        {
            TempData["AdminStatus"] = "Choose a user and a role before saving.";
            return RedirectToAction(nameof(Users));
        }

        var normalizedRole = role.Trim();
        if (!AppRoles.All.Contains(normalizedRole, StringComparer.OrdinalIgnoreCase))
        {
            TempData["AdminStatus"] = "That role is not available.";
            return RedirectToAction(nameof(Users));
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            TempData["AdminStatus"] = "The selected user could not be found.";
            return RedirectToAction(nameof(Users));
        }

        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.Id == user.Id)
        {
            TempData["AdminStatus"] = "You cannot change your own role from this page.";
            return RedirectToAction(nameof(Users));
        }

        if (!await _roleManager.RoleExistsAsync(normalizedRole))
        {
            await _roleManager.CreateAsync(new IdentityRole(normalizedRole));
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        if (currentRoles.Count == 1 && string.Equals(currentRoles[0], normalizedRole, StringComparison.OrdinalIgnoreCase))
        {
            TempData["AdminStatus"] = "No role change was needed.";
            return RedirectToAction(nameof(Users));
        }

        if (currentRoles.Count > 0)
        {
            if (currentRoles.Contains(AppRoles.Admin, StringComparer.OrdinalIgnoreCase) && await IsLastAdminAsync(user))
            {
                TempData["AdminStatus"] = "You cannot remove the last admin role.";
                return RedirectToAction(nameof(Users));
            }

            var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
            {
                TempData["AdminStatus"] = "We could not clear the current role.";
                return RedirectToAction(nameof(Users));
            }
        }

        var addResult = await _userManager.AddToRoleAsync(user, normalizedRole);
        if (!addResult.Succeeded)
        {
            var error = addResult.Errors.FirstOrDefault()?.Description ?? "Unable to update the user role.";
            TempData["AdminStatus"] = error;
            return RedirectToAction(nameof(Users));
        }

        await _changeLogService.LogAsync(
            _context,
            "Employee",
            "RoleChanged",
            $"Changed role for {user.Email ?? user.UserName}",
            $"New role: {GetRoleLabel(normalizedRole)}",
            _userManager.GetUserId(User));

        TempData["AdminStatus"] = $"Updated {user.Email ?? user.UserName} to {GetRoleLabel(normalizedRole)}.";
        return RedirectToAction(nameof(Users));
    }

    public async Task<IActionResult> Roles()
    {
        var roleSummaries = new List<RoleSummaryViewModel>();

        foreach (var role in AppRoles.All)
        {
            var usersInRole = await _userManager.GetUsersInRoleAsync(role);

            roleSummaries.Add(new RoleSummaryViewModel
            {
                Value = role,
                Label = GetRoleLabel(role),
                Description = GetRoleDescription(role),
                UserCount = usersInRole.Count
            });
        }

        return View(new AdminRolesViewModel
        {
            Roles = roleSummaries
        });
    }

    public IActionResult Settings()
    {
        return View();
    }

    public async Task<IActionResult> Activity(int changePage = 1, int workbookPage = 1)
    {
        var entries = await _context.ChangeLogEntries.AsNoTracking()
            .ToListAsync();

        entries = entries
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .Take(100)
            .ToList();

        var actorIds = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ActorUserId))
            .Select(entry => entry.ActorUserId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var actorLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (actorIds.Count > 0)
        {
            var users = await _userManager.Users
                .Where(u => actorIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Email, u.UserName })
                .ToListAsync();

            var profiles = await _context.EmployeeProfiles.AsNoTracking()
                .Where(p => actorIds.Contains(p.UserId))
                .ToListAsync();

            var profileByUserId = profiles.ToDictionary(p => p.UserId, StringComparer.OrdinalIgnoreCase);
            actorLookup = users.ToDictionary(
                u => u.Id,
                u => profileByUserId.TryGetValue(u.Id, out var profile)
                    ? EmployeeDirectoryService.GetDisplayName(new IdentityUser { Id = u.Id, Email = u.Email, UserName = u.UserName }, profile)
                    : (string.IsNullOrWhiteSpace(u.Email) ? u.UserName ?? u.Id : u.Email),
                StringComparer.OrdinalIgnoreCase);
        }

        var model = entries.Select(entry => new ChangeLogRowViewModel
        {
            Id = entry.Id,
            ActorName = !string.IsNullOrWhiteSpace(entry.ActorUserId) && actorLookup.TryGetValue(entry.ActorUserId, out var name)
                ? name
                : "System",
            EntityType = entry.EntityType,
            Action = entry.Action,
            Summary = entry.Summary,
            Details = entry.Details,
            CreatedAtUtc = entry.CreatedAtUtc
        }).ToList();

        var rotations = await _context.Rotations
            .AsNoTracking()
            .Include(r => r.ScheduleEntries)
            .ToListAsync();

        var latestRotation = rotations
            .OrderByDescending(r => r.ImportedAtUtc)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var latestWorkbookEntries = latestRotation?.ScheduleEntries
            .Where(entry => entry.WorkDate == today)
            .OrderBy(entry => entry.WorkDate)
            .ThenBy(entry => entry.EmployeeName)
            .ThenBy(entry => entry.EmployeeCode)
            .ToList() ?? [];

        return View(new AdminActivityViewModel
        {
            ChangeLogs = PaginationHelper.Create(
                model,
                changePage,
                10,
                "Admin",
                nameof(Activity),
                "changePage",
                new Dictionary<string, object?>
                {
                    ["workbookPage"] = workbookPage
                }),
            LatestRotationId = latestRotation?.Id,
            LatestWorkbookTitle = latestRotation?.Title ?? string.Empty,
            LatestSheetName = latestRotation?.SourceSheetName ?? string.Empty,
            LatestImportedAtUtc = latestRotation?.ImportedAtUtc,
            LatestWorkbookEntries = PaginationHelper.Create(
                latestWorkbookEntries,
                workbookPage,
                10,
                "Admin",
                nameof(Activity),
                "workbookPage",
                new Dictionary<string, object?>
                {
                    ["changePage"] = changePage
                })
        });
    }

    public IActionResult ImportEmployees(int page = 1)
    {
        return View(new EmployeeImportResultViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportEmployees(IFormFile workbook, int page = 1)
    {
        if (workbook is null || workbook.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Choose the rotation workbook before importing.");
            return View(new EmployeeImportResultViewModel());
        }

        if (!await _roleManager.RoleExistsAsync(AppRoles.Employee))
        {
            await _roleManager.CreateAsync(new IdentityRole(AppRoles.Employee));
        }

        var rows = new List<EmployeeImportRowViewModel>();
        var errors = new List<string>();
        var createdUsers = 0;
        var updatedUsers = 0;
        var parsedRotation = default(RotationWorkbookImportResult);
        var importedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            using var uploadedStream = workbook.OpenReadStream();
            using var workbookBuffer = new MemoryStream();
            await uploadedStream.CopyToAsync(workbookBuffer);
            var workbookBytes = workbookBuffer.ToArray();

            using var parseStream = new MemoryStream(workbookBytes);
            parsedRotation = RotationWorkbookImporter.Read(parseStream);
            var userIdByEmployeeCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var employee in parsedRotation.Employees)
            {
                var email = BuildEmployeeEmail(employee.Code);
                var password = BuildEmployeePassword(employee.Code);
                var rowStatus = "Existing";
                var rowPassword = string.Empty;

                var profile = await _context.EmployeeProfiles
                    .FirstOrDefaultAsync(p => p.EmployeeCode == employee.Code);

                IdentityUser? existingUser = null;
                if (profile is not null)
                {
                    existingUser = await _userManager.FindByIdAsync(profile.UserId);
                }

                if (existingUser is null)
                {
                    if (profile is not null)
                    {
                        errors.Add($"Employee code {employee.Code} already exists but the linked account could not be found. Rotation rows for this code were skipped.");
                        rows.Add(new EmployeeImportRowViewModel
                        {
                            Code = employee.Code,
                            Name = employee.Name,
                            JobTitle = employee.JobTitle,
                            Email = string.Empty,
                            Password = string.Empty,
                            Status = "Skipped: existing code without account"
                        });
                        continue;
                    }

                    var user = new IdentityUser
                    {
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true
                    };

                    var createResult = await _userManager.CreateAsync(user, password);
                    if (!createResult.Succeeded)
                    {
                        var error = createResult.Errors.FirstOrDefault()?.Description ?? $"Unable to create account for {employee.Name}.";
                        errors.Add(error);
                        rows.Add(new EmployeeImportRowViewModel
                        {
                            Code = employee.Code,
                            Name = employee.Name,
                            JobTitle = employee.JobTitle,
                            Email = email,
                            Password = password,
                            Status = $"Failed: {error}"
                        });
                        continue;
                    }

                    await _userManager.AddToRoleAsync(user, AppRoles.Employee);
                    createdUsers++;
                    existingUser = user;
                    rowStatus = "Created";
                    rowPassword = password;

                    profile = new EmployeeProfile
                    {
                        UserId = existingUser.Id
                    };
                    _context.EmployeeProfiles.Add(profile);
                }
                else
                {
                    if (!await _userManager.IsInRoleAsync(existingUser, AppRoles.Employee))
                    {
                        await _userManager.AddToRoleAsync(existingUser, AppRoles.Employee);
                    }

                    updatedUsers++;
                    rowStatus = "Updated rotation";
                }

                userIdByEmployeeCode[employee.Code] = existingUser.Id;
                var displayEmail = existingUser.Email ?? email;
                profile ??= new EmployeeProfile
                {
                    UserId = existingUser.Id
                };

                profile.ArabicName = employee.Name;
                profile.EmployeeCode = employee.Code;
                profile.JobTitle = employee.JobTitle;
                profile.UpdatedAtUtc = importedAtUtc;

                rows.Add(new EmployeeImportRowViewModel
                {
                    Code = employee.Code,
                    Name = employee.Name,
                    JobTitle = employee.JobTitle,
                    Email = displayEmail,
                    Password = rowPassword,
                    Status = rowStatus
                });
            }

            var rotation = await _context.Rotations
                .Include(r => r.ScheduleEntries)
                .FirstOrDefaultAsync(r => r.SourceSheetName == parsedRotation.SheetName && r.Title == parsedRotation.RotationTitle);

            if (rotation is null)
            {
                rotation = new Rotation
                {
                    Title = parsedRotation.RotationTitle,
                    Description = $"Imported from {workbook.FileName} ({parsedRotation.StartDate:yyyy-MM-dd} to {parsedRotation.EndDate:yyyy-MM-dd})",
                    StartAt = new DateTimeOffset(parsedRotation.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                    EndAt = new DateTimeOffset(parsedRotation.EndDate.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero),
                    Status = RotationStatus.Active,
                    CreatedAtUtc = importedAtUtc,
                    ImportedAtUtc = importedAtUtc,
                    SourceSheetName = parsedRotation.SheetName,
                    SourceWorkbookTitle = workbook.FileName
                };

                _context.Rotations.Add(rotation);
            }
            else
            {
                if (rotation.ScheduleEntries.Count > 0)
                {
                    _context.RotationScheduleEntries.RemoveRange(rotation.ScheduleEntries);
                }

                rotation.Description = $"Imported from {workbook.FileName} ({parsedRotation.StartDate:yyyy-MM-dd} to {parsedRotation.EndDate:yyyy-MM-dd})";
                rotation.StartAt = new DateTimeOffset(parsedRotation.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                rotation.EndAt = new DateTimeOffset(parsedRotation.EndDate.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero);
                rotation.Status = RotationStatus.Active;
                rotation.SourceWorkbookTitle = workbook.FileName;
                rotation.SourceSheetName = parsedRotation.SheetName;
                rotation.ImportedAtUtc = importedAtUtc;
            }

            var scheduleEntries = 0;
            foreach (var entry in parsedRotation.DayEntries)
            {
                if (!userIdByEmployeeCode.TryGetValue(entry.Code, out var userId))
                {
                    errors.Add($"No user account exists for employee code {entry.Code}; schedule entry on {entry.WorkDate:yyyy-MM-dd} was skipped.");
                    continue;
                }

                rotation.ScheduleEntries.Add(new RotationScheduleEntry
                {
                    EmployeeUserId = userId,
                    EmployeeCode = entry.Code,
                    EmployeeName = entry.Name,
                    JobTitle = entry.JobTitle,
                    WorkDate = entry.WorkDate,
                    OriginalStatus = entry.Status,
                    Status = entry.Status
                });
                scheduleEntries++;
            }

            await _context.SaveChangesAsync();

            await EmployeeDirectoryService.RecalculateLeaveImpactAsync(
                _context,
                rotation.ScheduleEntries.Select(entry => entry.EmployeeUserId));
            await _context.SaveChangesAsync();

            await _changeLogService.LogAsync(
                _context,
                "Rotation",
                "Imported",
                $"Imported rotation {rotation.Title}",
                $"Sheet: {parsedRotation.SheetName}; Employees: {parsedRotation.Employees.Count}; Entries: {scheduleEntries}",
                _userManager.GetUserId(User));

            return View(new EmployeeImportResultViewModel
            {
                ParsedRows = parsedRotation.Employees.Count,
                CreatedUsers = createdUsers,
                UpdatedUsers = updatedUsers,
                SheetName = parsedRotation.SheetName,
                RotationTitle = parsedRotation.RotationTitle,
                RotationStart = parsedRotation.StartDate,
                RotationEnd = parsedRotation.EndDate,
                ImportedAtUtc = importedAtUtc,
                ScheduleEntries = scheduleEntries,
                Rows = PaginationHelper.Create(rows, page, 10, "Admin", nameof(ImportEmployees)),
                Errors = errors
            });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"Unable to read the workbook: {ex.Message}");
            return View(new EmployeeImportResultViewModel
            {
                ImportedAtUtc = importedAtUtc,
                Rows = PaginationHelper.Create(rows, page, 10, "Admin", nameof(ImportEmployees)),
                Errors = errors
            });
        }
    }

    private static List<RoleOptionViewModel> GetRoleOptions() =>
    [
        new RoleOptionViewModel
        {
            Value = AppRoles.Admin,
            Label = "Admin",
            Description = "Full control across the system."
        },
        new RoleOptionViewModel
        {
            Value = AppRoles.Manager,
            Label = "Supervisor",
            Description = "Approves and manages team operations."
        },
        new RoleOptionViewModel
        {
            Value = AppRoles.Employee,
            Label = "User",
            Description = "Standard employee self-service access."
        }
    ];

    private static string GetRoleLabel(string role) =>
        string.Equals(role, AppRoles.Manager, StringComparison.OrdinalIgnoreCase) ? "Supervisor" :
        string.Equals(role, AppRoles.Employee, StringComparison.OrdinalIgnoreCase) ? "User" :
        "Admin";

    private static string GetRoleDescription(string role) =>
        string.Equals(role, AppRoles.Admin, StringComparison.OrdinalIgnoreCase)
            ? "Full access to all system areas."
            : string.Equals(role, AppRoles.Manager, StringComparison.OrdinalIgnoreCase)
                ? "Reviews leave, tasks, and rotations for the team."
                : "Standard self-service access for employees.";

    private async Task<AdminUserEditViewModel> BuildEditViewModelAsync(IdentityUser user, AdminUserEditViewModel? model = null)
    {
        var currentRole = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? AppRoles.Employee;
        var profile = await _context.EmployeeProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id);

        return new AdminUserEditViewModel
        {
            Id = user.Id,
            Email = model?.Email ?? user.Email ?? user.UserName ?? string.Empty,
            ArabicName = model?.ArabicName ?? profile?.ArabicName ?? string.Empty,
            EmployeeCode = model?.EmployeeCode ?? profile?.EmployeeCode ?? string.Empty,
            JobTitle = model?.JobTitle ?? profile?.JobTitle ?? string.Empty,
            UserName = user.UserName ?? user.Email ?? string.Empty,
            NewPassword = string.Empty,
            CurrentRole = currentRole,
            CurrentRoleLabel = GetRoleLabel(currentRole),
            IsCurrentUser = string.Equals(_userManager.GetUserId(User) ?? string.Empty, user.Id, StringComparison.OrdinalIgnoreCase)
        };
    }

    private async Task<bool> IsLastAdminAsync(IdentityUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        if (!roles.Contains(AppRoles.Admin, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var adminUsers = await _userManager.GetUsersInRoleAsync(AppRoles.Admin);
        return adminUsers.Count <= 1;
    }

    private static string GetUserDisplayName(IdentityUser user, EmployeeProfile? profile = null) =>
        EmployeeDirectoryService.GetDisplayName(user, profile);

    private static string BuildEmployeeEmail(string code) =>
        $"{code}@bahr-elbaqar.local";

    private static string BuildEmployeePassword(string code) =>
        $"Emp@{code}";
}
