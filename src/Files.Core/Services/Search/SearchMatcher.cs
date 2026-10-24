using Files.Core.Models;

namespace Files.Core.Services.Search;

/// <summary>
/// 判断单个条目是否满足查询条件。
/// </summary>
/// <remarks>
/// <b>解析器与匹配器分离</b>（SEARCH.md §5）：
/// 筛选（当前目录）与递归搜索共用这一个匹配器，
/// 保证"筛选能找到的，搜索也能找到"，不会出现两套不一致的判断逻辑。
/// </remarks>
public sealed class SearchMatcher
{
    private static readonly string[] PictureExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico", ".svg", ".heic"];

    private static readonly string[] DocumentExtensions =
        [".txt", ".md", ".log", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".csv", ".rtf", ".odt"];

    private static readonly string[] VideoExtensions =
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg"];

    private static readonly string[] AudioExtensions =
        [".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a", ".opus"];

    private readonly SearchQuery _query;
    private readonly DateTimeOffset _now;

    /// <summary>创建匹配器。</summary>
    /// <param name="query">查询。</param>
    /// <param name="now">当前时间（测试可注入固定值）。</param>
    public SearchMatcher(SearchQuery query, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(query);

        _query = query;
        _now = now ?? DateTimeOffset.Now;
    }

    /// <summary>查询是否为空（空查询匹配全部）。</summary>
    public bool IsEmpty => _query.IsEmpty;

    /// <summary>判断条目是否匹配。</summary>
    public bool IsMatch(FileSystemEntry entry)
    {
        foreach (SearchPredicate predicate in _query.Predicates)
        {
            if (!Evaluate(predicate, entry))
            {
                return false;
            }
        }

        return true;
    }

    private bool Evaluate(SearchPredicate predicate, FileSystemEntry entry) => predicate switch
    {
        NameContains name => entry.Name.Contains(name.Text, StringComparison.OrdinalIgnoreCase),
        ExtensionIs ext => !entry.IsDirectory
            && string.Equals(entry.Extension, ext.Extension, StringComparison.OrdinalIgnoreCase),
        KindIs kind => MatchesKind(entry, kind.Kind),
        SizeInRange range => MatchesSize(entry, range),
        ModifiedIn date => MatchesDate(entry, date.Range),
        Not not => !Evaluate(not.Inner, entry),
        _ => true,
    };

    private static bool MatchesKind(FileSystemEntry entry, EntryKindFilter kind)
    {
        if (kind == EntryKindFilter.Folder)
        {
            return entry.IsDirectory;
        }

        if (entry.IsDirectory)
        {
            return false;
        }

        string ext = entry.Extension.ToLowerInvariant();

        return kind switch
        {
            EntryKindFilter.Picture => PictureExtensions.Contains(ext),
            EntryKindFilter.Document => DocumentExtensions.Contains(ext),
            EntryKindFilter.Video => VideoExtensions.Contains(ext),
            EntryKindFilter.Audio => AudioExtensions.Contains(ext),
            EntryKindFilter.Other => !PictureExtensions.Contains(ext)
                                     && !DocumentExtensions.Contains(ext)
                                     && !VideoExtensions.Contains(ext)
                                     && !AudioExtensions.Contains(ext),
            _ => true,
        };
    }

    private static bool MatchesSize(FileSystemEntry entry, SizeInRange range)
    {
        // 文件夹没有有意义的大小，size: 条件只匹配文件
        if (entry.IsDirectory)
        {
            return false;
        }

        if (range.MinBytes is long min && entry.SizeBytes < min)
        {
            return false;
        }

        if (range.MaxBytes is long max && entry.SizeBytes > max)
        {
            return false;
        }

        return true;
    }

    private bool MatchesDate(FileSystemEntry entry, RelativeDateRange range)
    {
        DateTimeOffset local = entry.LastModified.ToLocalTime();
        DateTimeOffset nowLocal = _now.ToLocalTime();

        DateTimeOffset start = range switch
        {
            RelativeDateRange.Today => new DateTimeOffset(nowLocal.Date, nowLocal.Offset),
            RelativeDateRange.ThisWeek => new DateTimeOffset(
                nowLocal.Date.AddDays(-(int)nowLocal.DayOfWeek + (nowLocal.DayOfWeek == DayOfWeek.Sunday ? -6 : 1)),
                nowLocal.Offset),
            RelativeDateRange.ThisMonth => new DateTimeOffset(
                new DateTime(nowLocal.Year, nowLocal.Month, 1),
                nowLocal.Offset),
            RelativeDateRange.ThisYear => new DateTimeOffset(
                new DateTime(nowLocal.Year, 1, 1),
                nowLocal.Offset),
            _ => DateTimeOffset.MinValue,
        };

        return local >= start;
    }
}
