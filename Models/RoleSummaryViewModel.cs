namespace HRSystem.Models;

public sealed class RoleSummaryViewModel
{
    public required string Value { get; init; }

    public required string Label { get; init; }

    public required string Description { get; init; }

    public required int UserCount { get; init; }
}
