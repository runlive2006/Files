using System.Runtime.InteropServices;
using Files.Core.Services;

namespace Files.Interop;

/// <summary>
/// 基于 <c>FindFirstFileExW</c> / <c>FindNextFileW</c> 的目录枚举。
/// </summary>
/// <remarks>
/// 相比 <see cref="Directory.EnumerateFileSystemEntries(string)"/>，这条路径：
/// <list type="bullet">
/// <item>一次系统调用即拿到名称、属性、大小、修改时间，<b>无需为每项额外 stat</b>；</item>
/// <item>用 <c>FindExInfoBasic</c> 跳过 8.3 短名，用 <c>FIND_FIRST_EX_LARGE_FETCH</c> 批量取回；</item>
/// <item>返回结构体而非 <see cref="FileSystemInfo"/>，避免每项一个托管对象。</item>
/// </list>
/// </remarks>
public static class Win32DirectoryEnumerator
{
    /// <summary>
    /// 枚举目录下的直接子项（惰性）。枚举过程中句柄由 <see cref="SafeFindHandle"/> 保证释放。
    /// </summary>
    /// <param name="directoryPath">目录路径。</param>
    /// <exception cref="DirectoryNotFoundException">目录不存在。</exception>
    /// <exception cref="UnauthorizedAccessException">无权访问。</exception>
    public static IEnumerable<FileEntry> Enumerate(string directoryPath)
    {
        foreach (var entry in ReadAll(directoryPath))
        {
            yield return entry;
        }
    }

    /// <summary>
    /// 一次性读入目录下的全部子项。热路径使用此方法，避免迭代器开销。
    /// </summary>
    /// <param name="directoryPath">目录路径。</param>
    /// <returns>条目数组；空目录返回空数组。</returns>
    /// <exception cref="DirectoryNotFoundException">目录不存在。</exception>
    /// <exception cref="UnauthorizedAccessException">无权访问。</exception>
    public static FileEntry[] ReadAll(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        string fullPath = Path.GetFullPath(directoryPath);

        // 必须先区分"目录不存在"与"目录为空"：两者在 FindFirstFileEx 里都是 ERROR_FILE_NOT_FOUND。
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"目录不存在：{directoryPath}");
        }

        string searchPattern = PathUtility.EnsureTrailingSeparator(fullPath) + "*";
        string nativePath = PathUtility.NeedsExtendedPrefix(searchPattern)
            ? PathUtility.ToExtendedPath(searchPattern)
            : searchPattern;

        SafeFindHandle handle = NativeMethods.FindFirstFileEx(
            nativePath,
            NativeMethods.FindExInfoBasic,
            out Win32FindData data,
            fSearchOp: 0,
            lpSearchFilter: IntPtr.Zero,
            dwAdditionalFlags: NativeMethods.FindFirstExLargeFetch);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();

            return error switch
            {
                NativeMethods.ErrorFileNotFound or NativeMethods.ErrorNoMoreFiles => [],
                NativeMethods.ErrorAccessDenied => throw new UnauthorizedAccessException($"无权访问目录：{directoryPath}"),
                _ => throw new IOException($"枚举目录失败：{directoryPath}（Win32 错误 {error}）"),
            };
        }

        try
        {
            var entries = new List<FileEntry>(capacity: 64);

            while (true)
            {
                if (!IsDotEntry(data.FileName))
                {
                    entries.Add(new FileEntry(
                        data.FileName,
                        data.FileAttributes,
                        data.Size,
                        data.LastWriteUtc));
                }

                if (!NativeMethods.FindNextFile(handle, out data))
                {
                    int error = Marshal.GetLastWin32Error();

                    // 正常结束是 ERROR_NO_MORE_FILES；其他错误不应静默吞掉。
                    if (error != NativeMethods.ErrorNoMoreFiles && error != 0)
                    {
                        throw new IOException($"枚举目录中断：{directoryPath}（Win32 错误 {error}）");
                    }

                    break;
                }
            }

            return [.. entries];
        }
        finally
        {
            handle.Dispose();
        }
    }

    private static bool IsDotEntry(string name) =>
        name is "." or ".." || string.IsNullOrEmpty(name);
}
