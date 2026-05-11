namespace HRSystem.Models;

public enum TaskItemStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Cancelled = 3
}

public class TaskItem
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public TaskItemStatus Status { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateOnly? DueDate { get; set; }

    public string? AssignedToUserId { get; set; }

    public int? RotationId { get; set; }

    public Rotation? Rotation { get; set; }
}
