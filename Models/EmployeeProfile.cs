using Microsoft.AspNetCore.Identity;

namespace HRSystem.Models;

public class EmployeeProfile
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public IdentityUser? User { get; set; }

    public string ArabicName { get; set; } = string.Empty;

    public string EmployeeCode { get; set; } = string.Empty;

    public string JobTitle { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
