using System.ComponentModel.DataAnnotations;

namespace HRSystem.Models;

public sealed class AdminUserEditViewModel
{
    public required string Id { get; init; }

    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Arabic name")]
    public string ArabicName { get; set; } = string.Empty;

    [Display(Name = "Employee code")]
    public string EmployeeCode { get; set; } = string.Empty;

    [Display(Name = "Job title")]
    public string JobTitle { get; set; } = string.Empty;

    public string UserName { get; init; } = string.Empty;

    [Display(Name = "New password")]
    public string? NewPassword { get; set; }

    public string CurrentRole { get; init; } = string.Empty;

    public string CurrentRoleLabel { get; init; } = string.Empty;

    public bool IsCurrentUser { get; init; }
}
