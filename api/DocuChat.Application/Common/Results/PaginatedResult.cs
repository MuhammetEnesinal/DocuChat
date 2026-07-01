namespace DocuChat.Application.Common.Results;

public class PaginatedResult<T>
{
    public IReadOnlyList<T> Items { get; set; }
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }

    public PaginatedResult(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
    {
        this.Items = Items;
        this.TotalCount = TotalCount;
        this.Page = Page;
        this.PageSize = PageSize;
    }

    public bool HasNextPage => Page * PageSize < TotalCount;
    public bool HasPreviousPage => Page > 1;
}
