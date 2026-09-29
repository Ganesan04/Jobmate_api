namespace Jobmate.Domain.Common;

/// <summary>
/// Read-side projection with paging metadata, serialised as
/// <c>{ success, data, pagination }</c> by the API layer.
/// </summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalItems { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalItems / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;

    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalItems) =>
        new() { Items = items, Page = page, PageSize = pageSize, TotalItems = totalItems };

    public static PagedResult<T> Empty(int page, int pageSize) =>
        new() { Items = [], Page = page, PageSize = pageSize, TotalItems = 0 };

    public PagedResult<TOut> Map<TOut>(Func<T, TOut> selector) =>
        PagedResult<TOut>.Create(Items.Select(selector).ToList(), Page, PageSize, TotalItems);
}
