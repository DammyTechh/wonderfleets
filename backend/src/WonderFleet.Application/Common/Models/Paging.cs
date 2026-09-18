namespace WonderFleet.Application.Common.Models;

public record PageQuery
{
    public const int MaxPageSize = 100;
    private int _page = 1;
    private int _pageSize = 10;

    public int Page { get => _page; init => _page = value < 1 ? 1 : value; }
    public int PageSize { get => _pageSize; init => _pageSize = value is < 1 ? 10 : Math.Min(value, MaxPageSize); }
    public string? Search { get; init; }

    public int Skip => (Page - 1) * PageSize;
    public string? NormalizedSearch => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim().ToLowerInvariant();
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}

public sealed record IdResponse(Guid Id, string? Code = null);
