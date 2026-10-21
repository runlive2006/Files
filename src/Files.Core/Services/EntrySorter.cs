using Files.Core.Models;

namespace Files.Core.Services;

/// <summary>可排序的列。</summary>
public enum EntrySortColumn
{
    /// <summary>名称。</summary>
    Name,

    /// <summary>大小。</summary>
    Size,

    /// <summary>修改时间。</summary>
    LastModified,

    /// <summary>类型（扩展名）。</summary>
    Kind,
}

/// <summary>
/// 目录条目排序。
/// </summary>
/// <remarks>
/// 两条与资源管理器对齐的规则：
/// <list type="number">
/// <item>文件夹永远排在文件之前，即使正在降序排列（降序只作用于同组内部）；</item>
/// <item>键值相同时按名称升序兜底，保证排序结果稳定、可重复。</item>
/// </list>
/// </remarks>
public static class EntrySorter
{
    /// <summary>
    /// 返回排序后的新列举结果（不修改入参）。
    /// </summary>
    /// <param name="listing">原始列举结果。</param>
    /// <param name="column">排序列。</param>
    /// <param name="descending">是否降序。</param>
    public static DirectoryListing Sort(DirectoryListing listing, EntrySortColumn column, bool descending)
    {
        ArgumentNullException.ThrowIfNull(listing);

        FileSystemEntry[] entries = (FileSystemEntry[])listing.Entries.Clone();

        // "类型"排序需要反复比较扩展名，而 Extension 是现算属性（会分配字符串）。
        // 因此先一次性把键算出来，避免 O(n log n) 次分配。
        if (column == EntrySortColumn.Kind)
        {
            SortByKind(entries, descending);
        }
        else
        {
            Array.Sort(entries, (left, right) => Compare(left, right, column, descending));
        }

        return listing.WithEntries(entries);
    }

    private static void SortByKind(FileSystemEntry[] entries, bool descending)
    {
        var decorated = new (string Key, FileSystemEntry Entry)[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            decorated[i] = (entries[i].Extension, entries[i]);
        }

        Array.Sort(decorated, (left, right) =>
        {
            if (left.Entry.IsDirectory != right.Entry.IsDirectory)
            {
                return left.Entry.IsDirectory ? -1 : 1;
            }

            int byKey = StringComparer.OrdinalIgnoreCase.Compare(left.Key, right.Key);
            if (byKey != 0)
            {
                return descending ? -byKey : byKey;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(left.Entry.Name, right.Entry.Name);
        });

        for (int i = 0; i < decorated.Length; i++)
        {
            entries[i] = decorated[i].Entry;
        }
    }

    private static int Compare(
        FileSystemEntry left,
        FileSystemEntry right,
        EntrySortColumn column,
        bool descending)
    {
        if (left.IsDirectory != right.IsDirectory)
        {
            return left.IsDirectory ? -1 : 1;
        }

        int result = column switch
        {
            EntrySortColumn.Name => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name),
            EntrySortColumn.Size => left.SizeBytes.CompareTo(right.SizeBytes),
            EntrySortColumn.LastModified => left.LastModified.CompareTo(right.LastModified),
            _ => 0,
        };

        if (result != 0)
        {
            return descending ? -result : result;
        }

        // 键值相同：名称升序兜底，保证稳定
        return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
    }
}
