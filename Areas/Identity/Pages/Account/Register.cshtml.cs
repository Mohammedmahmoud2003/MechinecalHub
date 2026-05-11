using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using HRSystem.Data;
using HRSystem.Infrastructure;

namespace HRSystem.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class RegisterModel(
    ApplicationDbContext context,
    UserManager<IdentityUser> userManager,
    RoleManager<IdentityRole> roleManager,
    SignInManager<IdentityUser> signInManager,
    ILogger<RegisterModel> logger) : PageModel
{
    private readonly ApplicationDbContext _context = context;
    private readonly UserManager<IdentityUser> _userManager = userManager;
    private readonly RoleManager<IdentityRole> _roleManager = roleManager;
    private readonly SignInManager<IdentityUser> _signInManager = signInManager;
    private readonly ILogger<RegisterModel> _logger = logger;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var normalizedCode = Input.EmployeeCode.Trim();
        if (await _context.EmployeeProfiles.AnyAsync(p => p.EmployeeCode == normalizedCode))
        {
            ModelState.AddModelError(nameof(Input.EmployeeCode), "That employee code is already in use.");
            return Page();
        }

        var user = new IdentityUser
        {
            UserName = Input.Email,
            Email = Input.Email
        };

        var result = await _userManager.CreateAsync(user, Input.Password);
        if (result.Succeeded)
        {
            if (await _roleManager.RoleExistsAsync(AppRoles.Employee))
            {
                await _userManager.AddToRoleAsync(user, AppRoles.Employee);
            }

            _context.EmployeeProfiles.Add(new Models.EmployeeProfile
            {
                UserId = user.Id,
                ArabicName = Input.ArabicName.Trim(),
                EmployeeCode = normalizedCode,
                JobTitle = Input.JobTitle.Trim(),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

            await _context.SaveChangesAsync();

            _logger.LogInformation("User created a new account with password.");
            await _signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect(ReturnUrl ?? IdentityLanding.GetPath([AppRoles.Employee]));
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return Page();
    }

    public sealed class InputModel
    {
        [Required]
        [Display(Name = "Arabic name")]
        public string ArabicName { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Code")]
        public string EmployeeCode { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Job title")]
        public string JobTitle { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
