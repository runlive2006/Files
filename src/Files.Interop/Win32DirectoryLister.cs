using Files.Core.Models;
using Files.Core.Services;

namespace Files.Interop;

/// <summary>
/// 使用 <see cref="Win32DirectoryEnumerator"/> 的 <see cref="IDirectoryLister"/> 实现。
/// </summary>
/// <remarks>
/// 排序规则与 <see cref="FileSystemDirectoryLister"/> 保持一致：文件夹优先，再按名称忽略大小写。
/// 两者的等价性由单元测试保证。
/// </remarks>
public sealed class Win32DirectoryLister : IDirectoryLister
{
    /// <inheritdoc />
    public IReadOnlyList<FileSystemEntry> List(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        FileEntry[] raw = Win32DirectoryEnumerator.ReadAll(directoryPath);
        var entries = new List<FileSystemEntry>(raw.Length);
        string basePath = PathUtility.EnsureTrailingSeparator(Path.GetFullPath(directoryPath));

        foreach (var item in raw)
        {
            entries.Add(new FileSystemEntry
            {
                FullPath = basePath + item.Name,
                Name = item.Name,
                IsDirectory = item.IsDirectory,
                SizeBytes = item.Size,
                LastModified = item.LastWriteUtc,
            });
        }

        entries.Sort(static (left, right) =>
        {
            if (left.IsDirectory != right.IsDirectory)
            {
                return left.IsDirectory ? -1 : 1;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        });

        return entries;
    }
}
