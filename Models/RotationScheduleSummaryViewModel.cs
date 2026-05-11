namespace HRSystem.Models;

public sealed class RotationScheduleSummaryViewModel
{
    public string EmployeeCode { get; init; } = string.Empty;

    public string EmployeeName { get; init; } = string.Empty;

    public string JobTitle { get; init; } = string.Empty;

    public int Days { get; init; }

    public DateOnly StartDate { get; init; }

    public DateOnly EndDate { get; init; }

    public int LeaveCount { get; init; }

    public int WorkingCount { get; init; }
}
