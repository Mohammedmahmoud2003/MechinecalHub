namespace HRSystem.Models;

public sealed class LeavePendingViewModel
{
    public PagedResultViewModel<LeaveRequest> Requests { get; init; } = new();
}
