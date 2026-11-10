namespace Meducate.API.Infrastructure;

internal sealed record PaginatedResponse<T>
{
    public IEnumerable<T> Items { get; }
    public int TotalCount { get; }
    public int Page { get; }
    public int PageSize { get; }
    public int TotalPages { get; }
    public bool HasNextPage { get; }

    public PaginatedResponse(IEnumerable<T> items, int totalCount, int skip = 0, int take = 0)
    {
        Items = items;
        TotalCount = totalCount;
        PageSize = take > 0 ? take : totalCount;
        Page = PageSize > 0 ? (skip / PageSize) + 1 : 1;
        TotalPages = PageSize > 0 ? (int)Math.Ceiling((double)totalCount / PageSize) : 1;
        HasNextPage = Page < TotalPages;
    }
}
