namespace HRSystem.Models;

public sealed class AdminUserRowViewModel
{
    public required string Id { get; init; }

    public required string Email { get; init; }

    public required string UserName { get; init; }

    public string ArabicName { get; init; } = string.Empty;

    public string EmployeeCode { get; init; } = string.Empty;

    public string JobTitle { get; init; } = string.Empty;

    public required string CurrentRole { get; init; }

    public required string CurrentRoleLabel { get; init; }

    public bool IsCurrentUser { get; init; }
}
