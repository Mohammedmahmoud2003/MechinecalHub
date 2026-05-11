namespace HRSystem.Models;

public sealed class RotationsIndexViewModel
{
    public PagedResultViewModel<Rotation> Rotations { get; init; } = new();

    public int PlannedCount { get; init; }

    public int ActiveCount { get; init; }

    public int CompletedCount { get; init; }
}
