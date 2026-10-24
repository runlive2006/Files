using Files.Core.Models;
using Files.Core.Services.Search;

namespace Files.Core.Services;

/// <summary>
/// 在已列举的结果上做内存筛选。
/// </summary>
/// <remarks>
/// <b>筛选不重新枚举目录</b>（SEARCH.md §2.3）：
/// 直接在当前 <see cref="DirectoryListing"/> 的条目数组上过滤，
/// 10 万项时单次筛选应 ≤ 50ms（纯字符串/数值比较，无额外分配）。
/// <para>
/// 与 <see cref="EntrySorter"/> 组合使用：先筛选再排序，或排序后筛选都成立，
/// 因为两者都不修改入参数组（都返回新结果）。
/// </para>
/// </remarks>
public static class EntryFilter
{
    /// <summary>
    /// 按查询筛选条目。
    /// </summary>
    /// <param name="listing">原始列举结果。</param>
    /// <param name="query">查询；为空时原样返回。</param>
    /// <param name="now">当前时间（测试可注入）。</param>
    public static DirectoryListing Apply(DirectoryListing listing, SearchQuery query, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(query);

        if (query.IsEmpty)
        {
            return listing;
        }

        var matcher = new SearchMatcher(query, now);

        // 预分配与原始相同容量，避免多次扩容
        var matched = new List<FileSystemEntry>(listing.Count);

        foreach (FileSystemEntry entry in listing.Entries)
        {
            if (matcher.IsMatch(entry))
            {
                matched.Add(entry);
            }
        }

        return listing.WithEntries([.. matched]);
    }
}
