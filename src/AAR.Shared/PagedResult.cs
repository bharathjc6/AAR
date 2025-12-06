// =============================================================================
// AAR.Shared - PagedResult.cs
// Pagination wrapper for list endpoints
// =============================================================================

namespace AAR.Shared;

/// <summary>
/// Represents a paginated result set
/// </summary>
/// <typeparam name="T">The type of items in the result</typeparam>
public class PagedResult<T>
{
    /// <summary>
    /// The items in the current page
    /// </summary>
    public IReadOnlyList<T> Items { get; }
    
    /// <summary>
    /// Total number of items across all pages
    /// </summary>
    public int TotalCount { get; }
    
    /// <summary>
    /// Current page number (1-based)
    /// </summary>
    public int Page { get; }
    
    /// <summary>
    /// Number of items per page
    /// </summary>
    public int PageSize { get; }
    
    /// <summary>
    /// Total number of pages
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    
    /// <summary>
    /// Whether there are more pages after the current one
    /// </summary>
    public bool HasNextPage => Page < TotalPages;
    
    /// <summary>
    /// Whether there are pages before the current one
    /// </summary>
    public bool HasPreviousPage => Page > 1;

    public PagedResult(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    /// <summary>
    /// Creates an empty paged result
    /// </summary>
    public static PagedResult<T> Empty(int page = 1, int pageSize = 10) =>
        new([], 0, page, pageSize);

    /// <summary>
    /// Maps items to a different type
    /// </summary>
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> mapper) =>
        new(Items.Select(mapper).ToList(), TotalCount, Page, PageSize);
}

/// <summary>
/// Pagination parameters for list requests
/// </summary>
public record PaginationParams
{
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    /// <summary>
    /// Page number (1-based)
    /// </summary>
    public int Page
    {
        get => _page;
        init => _page = value < 1 ? 1 : value;
    }

    /// <summary>
    /// Number of items per page
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value < 1 ? DefaultPageSize : Math.Min(value, MaxPageSize);
    }

    /// <summary>
    /// Number of items to skip
    /// </summary>
    public int Skip => (Page - 1) * PageSize;
}
