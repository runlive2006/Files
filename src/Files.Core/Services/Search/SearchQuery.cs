using Files.Core.Models;

namespace Files.Core.Services.Search;

/// <summary>文件类型类别（用于 <c>kind:</c> 筛选）。</summary>
public enum EntryKindFilter
{
    /// <summary>文件夹。</summary>
    Folder,

    /// <summary>图片。</summary>
    Picture,

    /// <summary>文档。</summary>
    Document,

    /// <summary>视频。</summary>
    Video,

    /// <summary>音频。</summary>
    Audio,

    /// <summary>其他文件。</summary>
    Other,
}

/// <summary>相对时间范围（用于 <c>datemodified:</c>）。</summary>
public enum RelativeDateRange
{
    /// <summary>今天。</summary>
    Today,

    /// <summary>本周。</summary>
    ThisWeek,

    /// <summary>本月。</summary>
    ThisMonth,

    /// <summary>今年。</summary>
    ThisYear,
}

/// <summary>
/// 一个筛选条件。
/// </summary>
public abstract record SearchPredicate;

/// <summary>名称包含指定子串（忽略大小写）。</summary>
public sealed record NameContains(string Text) : SearchPredicate;

/// <summary>扩展名匹配（含点，忽略大小写）。</summary>
public sealed record ExtensionIs(string Extension) : SearchPredicate;

/// <summary>类型类别匹配。</summary>
public sealed record KindIs(EntryKindFilter Kind) : SearchPredicate;

/// <summary>大小范围（字节，含端点）。</summary>
public sealed record SizeInRange(long? MinBytes, long? MaxBytes) : SearchPredicate;

/// <summary>修改时间相对范围。</summary>
public sealed record ModifiedIn(RelativeDateRange Range) : SearchPredicate;

/// <summary>取反。</summary>
public sealed record Not(SearchPredicate Inner) : SearchPredicate;

/// <summary>
/// 解析后的查询。
/// </summary>
/// <param name="RawText">原始输入文本。</param>
/// <param name="Predicates">条件列表；空列表表示"匹配全部"。</param>
public sealed record SearchQuery(string RawText, IReadOnlyList<SearchPredicate> Predicates)
{
    /// <summary>空查询（匹配全部）。</summary>
    public static SearchQuery Empty { get; } = new(string.Empty, []);

    /// <summary>是否为空查询。</summary>
    public bool IsEmpty => Predicates.Count == 0;
}
