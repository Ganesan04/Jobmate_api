using System.Text.Json.Serialization;

namespace Jobmate.Application.Common;

/// <summary>
/// Uniform success envelope: <c>{ success: true, data, message }</c>. A
/// <c>pagination</c> object is added by <see cref="PagedResponse{T}"/>.
/// </summary>
public class ApiResponse
{
    public bool Success { get; init; } = true;

    public string Message { get; init; } = "Success";

    /// <summary>Client error code, present on failures so Flutter can branch without string matching.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; init; }
}

/// <inheritdoc cref="ApiResponse"/>
public sealed class ApiResponse<T> : ApiResponse
{
    public ApiResponse(T data, string? message = null)
    {
        Data = data;

        if (message is not null)
        {
            Message = message;
        }
    }

    public T Data { get; }
}

/// <summary>Paging metadata block, matching the documented <c>pagination</c> shape.</summary>
public sealed class PaginationMeta
{
    public int Page { get; init; }

    public int PageSize { get; init; }

    public int TotalItems { get; init; }

    public int TotalPages { get; init; }

    public bool HasPrevious { get; init; }

    public bool HasNext { get; init; }

    public static PaginationMeta From(Domain.Common.PagedResult<object> page) => new()
    {
        Page = page.Page,
        PageSize = page.PageSize,
        TotalItems = page.TotalItems,
        TotalPages = page.TotalPages,
        HasPrevious = page.HasPrevious,
        HasNext = page.HasNext
    };

    /// <summary>Builds metadata from any typed page, which <c>From(PagedResult&lt;object&gt;)</c> cannot accept.</summary>
    public static PaginationMeta From<T>(Domain.Common.PagedResult<T> page) => new()
    {
        Page = page.Page,
        PageSize = page.PageSize,
        TotalItems = page.TotalItems,
        TotalPages = page.TotalPages,
        HasPrevious = page.HasPrevious,
        HasNext = page.HasNext
    };
}

/// <summary>
/// Paginated success envelope:
/// <c>{ success: true, data: [], pagination: { page, pageSize, totalItems, totalPages } }</c>.
/// </summary>
public sealed class PagedResponse<T> : ApiResponse
{
    public PagedResponse(Domain.Common.PagedResult<T> page, string? message = null)
    {
        Data = page.Items;
        Message = message ?? "Success";
        Pagination = new PaginationMeta
        {
            Page = page.Page,
            PageSize = page.PageSize,
            TotalItems = page.TotalItems,
            TotalPages = page.TotalPages,
            HasPrevious = page.HasPrevious,
            HasNext = page.HasNext
        };
    }

    public IReadOnlyList<T> Data { get; }

    public PaginationMeta Pagination { get; }
}

/// <summary>
/// Field-level validation failure: <c>{ success: false, message, errors: [...] }</c>.
/// </summary>
public sealed class ApiErrorResponse : ApiResponse
{
    public ApiErrorResponse()
    {
        Success = false;
    }

    public IReadOnlyList<ApiError> Errors { get; init; } = [];
}

/// <summary>A single validation problem.</summary>
public sealed class ApiError
{
    public ApiError()
    {
    }

    public ApiError(string field, string message)
    {
        Field = field;
        Message = message;
    }

    /// <summary>Field path in camelCase, e.g. <c>endTime</c>. Empty for request-level problems.</summary>
    public string Field { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    /// <summary>Machine-readable code, e.g. <c>ValidationFailure</c>.</summary>
    public string Code { get; init; } = "ValidationFailure";
}
