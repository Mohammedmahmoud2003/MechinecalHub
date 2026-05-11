namespace HRSystem.Models;

public sealed class PagedResultViewModel<T>
{
    public List<T> Items { get; init; } = [];

    public PaginationViewModel Pagination { get; init; } = new();
}
