namespace HRSystem.Models;

public sealed class ChangeLogRowViewModel
{
    public int Id { get; init; }

    public string? ActorName { get; init; }

    public string EntityType { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string? Details { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}
