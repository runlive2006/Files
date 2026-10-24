using Files.Core.Models;
using Files.Core.Services.Search;

namespace Files.Core.Services;

/// <summary>搜索进度。</summary>
/// <param name="FoundCount">已找到的匹配项数。</param>
/// <param name="ScannedCount">已扫描的条目数。</param>
/// <param name="SkippedDirectoryCount">因无权限等原因跳过的目录数。</param>
public readonly record struct SearchProgress(int FoundCount, int ScannedCount, int SkippedDirectoryCount);

/// <summary>
/// 一条搜索命中。
/// </summary>
/// <remarks>
/// <b>必须携带完整路径</b>：搜索结果的条目分散在不同目录，
/// 无法像目录列举那样由"父目录 + 名称"推导。
/// 早期版本只返回 <see cref="FileSystemEntry"/>（仅名称），
/// 导致界面无法显示命中的真实位置。
/// </remarks>
/// <param name="Entry">条目信息。</param>
/// <param name="FullPath">完整路径。</param>
public readonly record struct SearchHit(FileSystemEntry Entry, string FullPath);

/// <summary>搜索结果。</summary>
/// <param name="Hits">匹配的条目及其完整路径。</param>
/// <param name="SkippedDirectoryCount">跳过的目录数。</param>
/// <param name="Truncated">是否因达到上限而截断。</param>
public sealed record SearchResult(
    IReadOnlyList<SearchHit> Hits,
    int SkippedDirectoryCount,
    bool Truncated)
{
    /// <summary>命中数量。</summary>
    public int Count => Hits.Count;
}

/// <summary>
/// 递归搜索目录树。
/// </summary>
/// <remarks>
/// 设计要点（SEARCH.md §3.3）：
/// <list type="bullet">
/// <item><b>迭代式 BFS</b>，不用递归 —— 深目录不会栈溢出；</item>
/// <item><b>不跟随符号链接/重解析点</b> —— 防止循环与跨盘爆炸；</item>
/// <item>无权限目录跳过并计数，不中断整个搜索；</item>
/// <item>每 200 项检查一次取消，保证取消能及时生效；</item>
/// <item>结果上限 5 万项，超过则截断并标记。</item>
/// </list>
/// </remarks>
public sealed class RecursiveSearcher
{
    /// <summary>结果数量上限。</summary>
    public const int MaxResults = 50_000;

    private const int CancellationCheckInterval = 200;

    /// <summary>
    /// 递归搜索。
    /// </summary>
    /// <param name="rootPath">搜索根目录。</param>
    /// <param name="query">查询条件。</param>
    /// <param name="includeHidden">是否包含隐藏项。</param>
    /// <param name="progress">进度回调（可能较频繁，调用方应自行节流）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public SearchResult Search(
        string rootPath,
        SearchQuery query,
        bool includeHidden,
        IProgress<SearchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(query);

        var matcher = new SearchMatcher(query);
        var found = new List<SearchHit>();
        int scanned = 0;
        int skipped = 0;
        bool truncated = false;

        var pending = new Queue<string>();
        pending.Enqueue(rootPath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string current = pending.Dequeue();

            IEnumerable<FileSystemInfo> entries;

            try
            {
                entries = new DirectoryInfo(current).EnumerateFileSystemInfos();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException
                                           or IOException
                                           or ArgumentException
                                           or PathTooLongException)
            {
                skipped++;
                continue;
            }

            // 用显式枚举器以便在异常发生时精确捕获
            IEnumerator<FileSystemInfo>? enumerator = null;

            try
            {
                enumerator = entries.GetEnumerator();

                while (true)
                {
                    FileSystemInfo info;

                    try
                    {
                        if (!enumerator.MoveNext())
                        {
                            break;
                        }

                        info = enumerator.Current;
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException
                                                   or IOException
                                                   or PathTooLongException)
                    {
                        // 枚举中途出错（如目录被删除）：跳过该目录剩余项
                        skipped++;
                        break;
                    }

                    scanned++;

                    if (scanned % CancellationCheckInterval == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    bool isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
                    bool isHidden = (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
                    bool isReparse = (info.Attributes & FileAttributes.ReparsePoint) != 0;

                    if (isHidden && !includeHidden)
                    {
                        continue;
                    }

                    var entry = new FileSystemEntry(
                        info.Name,
                        isDirectory,
                        isDirectory ? 0 : new FileInfo(info.FullName).Length,
                        info.LastWriteTimeUtc,
                        isHidden);

                    if (matcher.IsMatch(entry))
                    {
                        found.Add(new SearchHit(entry, info.FullName));

                        if (found.Count >= MaxResults)
                        {
                            truncated = true;
                            break;
                        }
                    }

                    // 重解析点需区分：真实链接不跟随（防循环），云占位符可进入。
                    // 不加这个区分，云盘里的文件将完全搜不到。
                    if (isDirectory && (!isReparse || ReparsePointPolicy.IsTraversable(info.FullName, info.Attributes)))
                    {
                        pending.Enqueue(info.FullName);
                    }

                    progress?.Report(new SearchProgress(found.Count, scanned, skipped));
                }
            }
            finally
            {
                enumerator?.Dispose();
            }

            if (truncated)
            {
                break;
            }
        }

        progress?.Report(new SearchProgress(found.Count, scanned, skipped));
        return new SearchResult(found, skipped, truncated);
    }
}
