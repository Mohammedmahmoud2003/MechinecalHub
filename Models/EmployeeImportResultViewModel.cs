namespace HRSystem.Models;

public sealed class EmployeeImportResultViewModel
{
    public int ParsedRows { get; init; }

    public int CreatedUsers { get; init; }

    public int UpdatedUsers { get; init; }

    public string RotationTitle { get; init; } = string.Empty;

    public string SheetName { get; init; } = string.Empty;

    public DateTimeOffset ImportedAtUtc { get; init; }

    public DateOnly? RotationStart { get; init; }

    public DateOnly? RotationEnd { get; init; }

    public int ScheduleEntries { get; init; }

    public PagedResultViewModel<EmployeeImportRowViewModel> Rows { get; init; } = new();

    public List<string> Errors { get; init; } = [];
}
