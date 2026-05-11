namespace HRSystem.Models;

public sealed class AdminActivityViewModel
{
    public PagedResultViewModel<ChangeLogRowViewModel> ChangeLogs { get; init; } = new();

    public string LatestWorkbookTitle { get; init; } = string.Empty;

    public string LatestSheetName { get; init; } = string.Empty;

    public int? LatestRotationId { get; init; }

    public DateTimeOffset? LatestImportedAtUtc { get; init; }

    public PagedResultViewModel<RotationScheduleEntry> LatestWorkbookEntries { get; init; } = new();
}
