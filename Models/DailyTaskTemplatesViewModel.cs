namespace HRSystem.Models;

public sealed class DailyTaskTemplatesViewModel
{
    public IReadOnlyList<DailyTaskTemplate> Templates { get; init; } = [];

    public int TotalCount => Templates.Count;
}
