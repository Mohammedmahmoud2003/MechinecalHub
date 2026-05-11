namespace HRSystem.Models;

public sealed class TasksIndexViewModel
{
    public PagedResultViewModel<TaskItem> Tasks { get; init; } = new();

    public int PendingCount { get; init; }

    public int InProgressCount { get; init; }

    public int CompletedCount { get; init; }

    public int TotalCount { get; init; }
}
