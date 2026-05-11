using Microsoft.AspNetCore.Routing;

namespace HRSystem.Models;

public sealed class PaginationViewModel
{
    public int PageNumber { get; init; }

    public int PageSize { get; init; }

    public int TotalCount { get; init; }

    public string Controller { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string PageQueryName { get; init; } = "page";

    public IDictionary<string, object?> RouteValues { get; init; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool HasPrevious => PageNumber > 1;

    public bool HasNext => PageNumber < TotalPages;

    public RouteValueDictionary BuildRouteValues(int pageNumber)
    {
        var values = new RouteValueDictionary(RouteValues)
        {
            [PageQueryName] = pageNumber
        };

        return values;
    }
}
