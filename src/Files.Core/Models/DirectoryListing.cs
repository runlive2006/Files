namespace Files.Core.Models;

/// <summary>
/// 一次目录列举的结果：目录路径 + 条目数组。
/// </summary>
/// <remarks>
/// 把"目录路径"提升到集合层级，是为了让每个 <see cref="FileSystemEntry"/>
/// 只存名称。这是把每项内存从约 427 字节压到 200 字节以下的关键改动。
/// </remarks>
public sealed class DirectoryListing
{
    /// <summary>创建列举结果。</summary>
    /// <param name="directoryPath">被列举的目录完整路径（不含尾部分隔符）。</param>
    /// <param name="entries">条目数组；调用方不得在之后修改其内容。</param>
    public DirectoryListing(string directoryPath, FileSystemEntry[] entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(entries);

        DirectoryPath = directoryPath;
        Entries = entries;
    }

    /// <summary>被列举的目录完整路径。</summary>
    public string DirectoryPath { get; }

    /// <summary>条目集合（文件夹优先、名称升序，除非调用方另行排序）。</summary>
    public FileSystemEntry[] Entries { get; }

    /// <summary>条目数量。</summary>
    public int Count => Entries.Length;

    /// <summary>取得指定条目的完整路径。</summary>
    public string GetFullPath(FileSystemEntry entry) =>
        Path.Combine(DirectoryPath, entry.Name);

    /// <summary>取得指定下标条目的完整路径。</summary>
    public string GetFullPath(int index) =>
        Path.Combine(DirectoryPath, Entries[index].Name);

    /// <summary>返回一个使用新条目数组的列举结果（用于排序后替换）。</summary>
    public DirectoryListing WithEntries(FileSystemEntry[] entries) => new(DirectoryPath, entries);
}
