namespace Files.Core.Services;

/// <summary>
/// 目录内容摘要。
/// </summary>
/// <param name="FolderCount">直接子文件夹数。</param>
/// <param name="FileCount">直接子文件数。</param>
public readonly record struct DirectorySummary(int FolderCount, int FileCount)
{
    /// <summary>直接子项总数。</summary>
    public int TotalCount => FolderCount + FileCount;

    /// <summary>是否为空目录。</summary>
    public bool IsEmpty => TotalCount == 0;

    /// <summary>生成可读描述，例如「3 个文件夹，12 个文件」。</summary>
    public string Describe()
    {
        if (IsEmpty)
        {
            return "空文件夹";
        }

        if (FolderCount == 0)
        {
            return $"{FileCount} 个文件";
        }

        if (FileCount == 0)
        {
            return $"{FolderCount} 个文件夹";
        }

        return $"{FolderCount} 个文件夹，{FileCount} 个文件";
    }
}

/// <summary>
/// 统计目录的直接子项数量。
/// </summary>
/// <remarks>
/// <b>只统计直接子项，不递归计算总大小</b>。这是刻意的性能取舍：
/// 递归统计 <c>C:\Windows</c> 这类目录需要遍历数万文件，耗时从数百毫秒到数十秒；
/// 而直接子项计数是一次 <c>FindFirstFileEx</c> 遍历，通常在毫秒级。
/// <para>
/// 递归统计若将来要做，必须做成显式的「计算大小」命令，并带进度与取消，
/// 不能放在选中即触发的详情窗格里。
/// </para>
/// </remarks>
public sealed class DirectorySummaryService
{
    /// <summary>
    /// 统计指定目录的直接子项。
    /// </summary>
    /// <param name="directoryPath">目录路径。</param>
    /// <returns>统计结果；目录不存在或无权限时返回 <see langword="null"/>。</returns>
    public DirectorySummary? Summarize(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return null;
        }

        try
        {
            var directory = new DirectoryInfo(directoryPath);
            if (!directory.Exists)
            {
                return null;
            }

            int folders = 0;
            int files = 0;

            foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
            {
                // 只看属性，不打开文件，开销很小
                if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
                    folders++;
                }
                else
                {
                    files++;
                }
            }

            return new DirectorySummary(folders, files);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
        {
            return null;
        }
    }
}
