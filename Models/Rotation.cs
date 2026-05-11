namespace HRSystem.Models;

public enum RotationStatus
{
    Planned = 0,
    Active = 1,
    Completed = 2,
    Cancelled = 3
}

public class Rotation
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTimeOffset StartAt { get; set; }

    public DateTimeOffset EndAt { get; set; }

    public RotationStatus Status { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ImportedAtUtc { get; set; }

    /// <summary>Identity user id of the person who owns or created this rotation.</summary>
    public string? OwnerUserId { get; set; }

    public ICollection<TaskItem> TaskItems { get; set; } = new List<TaskItem>();

    public string? SourceSheetName { get; set; }

    public string? SourceWorkbookTitle { get; set; }

    public ICollection<RotationScheduleEntry> ScheduleEntries { get; set; } = new List<RotationScheduleEntry>();
}
