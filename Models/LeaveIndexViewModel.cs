namespace HRSystem.Models;

public sealed class LeaveIndexViewModel
{
    public PagedResultViewModel<LeaveRequest> Requests { get; init; } = new();

    public int PendingCount { get; init; }

    public int ApprovedCount { get; init; }

    public int DraftCount { get; init; }
}
