namespace Jobmate.Domain.Common;

/// <summary>
/// Page and sort options shared by every list endpoint, so no request can ask for
/// an unbounded result set. Declared as a record so the typed request records in the
/// Application layer can inherit from it.
/// </summary>
public record PageRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    public int Page
    {
        get => _page;
        init => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    public int Skip => (Page - 1) * PageSize;

    public string? Sort { get; init; }

    /// <summary>Convenience for a request that also needs a take count instead of a skip.</summary>
    public int Take => PageSize;
}
