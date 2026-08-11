using Microsoft.EntityFrameworkCore;

namespace Msgifly.Web.Models;

/// <summary>Non-generic view of a paged result, so Views/Shared/_Pagination.cshtml can take one model type regardless of the item type.</summary>
public interface IPagedList
{
    int PageIndex { get; }
    int TotalPages { get; }
    bool HasPrevious { get; }
    bool HasNext { get; }
}

/// <summary>
/// Query-string-driven paging (?page=2), the pattern every list screen in Phase 1 shares —
/// this is the reusable-grid equivalent called for in master doc §7.2/§12, minus a heavy JS
/// grid framework: plain server-rendered pages with a shared pagination partial.
/// </summary>
public class PagedList<T> : IPagedList
{
    public List<T> Items { get; }
    public int PageIndex { get; }
    public int TotalPages { get; }
    public int TotalCount { get; }

    public PagedList(List<T> items, int totalCount, int pageIndex, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        PageIndex = pageIndex;
        TotalPages = pageSize == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
    }

    public bool HasPrevious => PageIndex > 1;
    public bool HasNext => PageIndex < TotalPages;

    public static async Task<PagedList<T>> CreateAsync(IQueryable<T> source, int pageIndex, int pageSize)
    {
        pageIndex = Math.Max(pageIndex, 1);
        var count = await source.CountAsync();
        var items = await source.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedList<T>(items, count, pageIndex, pageSize);
    }
}
