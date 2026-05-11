namespace HRSystem.Models;

public class ChangeLogEntry
{
    public int Id { get; set; }

    public string? ActorUserId { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string? Details { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
