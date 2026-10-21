using Files.Core.Models;

namespace Files.Core.Services;

/// <summary>
/// 提供目录列举能力，便于在 UI 层与真实文件系统之间隔离，并可在测试中替换。
/// </summary>
public interface IDirectoryLister
{
    /// <summary>
    /// 列举指定目录下的直接子条目。
    /// </summary>
    /// <param name="directoryPath">要列举的目录路径。</param>
    /// <returns>按“文件夹优先、再按名称”排序的条目集合。</returns>
    IReadOnlyList<FileSystemEntry> List(string directoryPath);
}

/// <summary>
/// 基于 <see cref="Directory"/> 与 <see cref="File"/> 的真实文件系统实现。
/// </summary>
public sealed class FileSystemDirectoryLister : IDirectoryLister
{
    /// <inheritdoc />
    public IReadOnlyList<FileSystemEntry> List(string directoryPath)
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

        return entries
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static FileSystemEntry ToEntry(FileSystemInfo info) => info switch
    {
        DirectoryInfo dir => new FileSystemEntry
        {
            FullPath = dir.FullName,
            Name = dir.Name,
            IsDirectory = true,
            LastModified = dir.LastWriteTimeUtc,
        },
        FileInfo file => new FileSystemEntry
        {
            FullPath = file.FullName,
            Name = file.Name,
            IsDirectory = false,
            SizeBytes = file.Length,
            LastModified = file.LastWriteTimeUtc,
        },
        _ => throw new NotSupportedException($"不支持的条目类型：{info.GetType().Name}"),
    };
}
