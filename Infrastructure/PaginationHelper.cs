using HRSystem.Models;

namespace HRSystem.Infrastructure;

public static class PaginationHelper
{
    public static PagedResultViewModel<T> Create<T>(
        IEnumerable<T> source,
        int pageNumber,
        int pageSize,
        string controller,
        string action,
        string pageQueryName = "page",
        IDictionary<string, object?>? routeValues = null)
    {
        var list = source.ToList();
        var totalCount = list.Count;
        var totalPages = pageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var currentPage = Math.Clamp(pageNumber, 1, totalPages);

        var items = pageSize <= 0
            ? list
            : list.Skip((currentPage - 1) * pageSize).Take(pageSize).ToList();

        return new PagedResultViewModel<T>
        {
            Items = items,
            Pagination = new PaginationViewModel
            {
                PageNumber = currentPage,
                PageSize = pageSize <= 0 ? totalCount : pageSize,
                TotalCount = totalCount,
                Controller = controller,
                Action = action,
                PageQueryName = pageQueryName,
                RouteValues = routeValues ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            }
        };
    }
}
