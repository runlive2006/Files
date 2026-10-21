using Files.Core.Models;

namespace Files.Core.Services;

/// <summary>
/// 提供目录列举能力。UI 层只依赖这个接口，因此可在测试中替换，
/// 也可在未来换成 Shell 命名空间实现（库、搜索结果、压缩包）。
/// </summary>
public interface IDirectoryLister
{
    /// <summary>
    /// 列举指定目录下的直接子条目。
    /// </summary>
    /// <param name="directoryPath">要列举的目录路径。</param>
    /// <returns>按"文件夹优先、再按名称"排序的列举结果。</returns>
    /// <exception cref="DirectoryNotFoundException">目录不存在。</exception>
    /// <exception cref="UnauthorizedAccessException">无权访问。</exception>
    DirectoryListing List(string directoryPath);
}

/// <summary>
/// 基于 <see cref="Directory"/> / <see cref="File"/> 的纯托管实现。
/// </summary>
/// <remarks>
/// 生产路径使用 <c>Files.Interop</c> 的 Win32 实现；保留这一份是为了：
/// 交叉验证两者结果一致（单元测试），以及在非 Windows 环境下跑核心测试。
/// </remarks>
public sealed class FileSystemDirectoryLister : IDirectoryLister
{
    /// <inheritdoc />
    public DirectoryListing List(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        var directory = new DirectoryInfo(directoryPath);
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException($"目录不存在：{directoryPath}");
        }

        var entries = new List<FileSystemEntry>();
        foreach (var info in directory.EnumerateFileSystemInfos())
        {
            entries.Add(ToEntry(info));
        }

        entries.Sort(EntryOrdering.FoldersFirstByName);

        return new DirectoryListing(directory.FullName, [.. entries]);
    }

    private static FileSystemEntry ToEntry(FileSystemInfo info) => info switch
    {
        DirectoryInfo dir => new FileSystemEntry(
            dir.Name,
            isDirectory: true,
            sizeBytes: 0,
            lastModified: dir.LastWriteTimeUtc),
        FileInfo file => new FileSystemEntry(
            file.Name,
            isDirectory: false,
            sizeBytes: file.Length,
            lastModified: file.LastWriteTimeUtc),
        _ => throw new NotSupportedException($"不支持的条目类型：{info.GetType().Name}"),
    };
}

/// <summary>
/// 条目排序比较器（文件夹优先、名称忽略大小写）。
/// </summary>
internal static class EntryOrdering
{
    /// <summary>文件夹优先，再按名称忽略大小写升序。</summary>
    internal static int FoldersFirstByName(FileSystemEntry left, FileSystemEntry right)
    {
        if (left.IsDirectory != right.IsDirectory)
        {
            return left.IsDirectory ? -1 : 1;
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
    }
}
