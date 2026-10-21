namespace Files.Core.Services;

/// <summary>拖放要执行的动作。</summary>
public enum DropAction
{
    /// <summary>复制。</summary>
    Copy,

    /// <summary>移动。</summary>
    Move,
}

/// <summary>
/// 拖放效果判定。
/// </summary>
/// <remarks>
/// 规则与资源管理器一致：
/// <list type="bullet">
/// <item>按住 <c>Ctrl</c> → 复制；</item>
/// <item>按住 <c>Shift</c> → 移动；</item>
/// <item>无修饰键 → <b>同卷移动、跨卷复制</b>。</item>
/// </list>
/// 最后一条是关键：跨卷"移动"在物理上必须是一次复制 + 删除，
/// 直接当移动处理会让用户误以为瞬间完成，实际在后台搬运大量数据。
/// 明确降级为复制更符合预期。
/// </remarks>
public static class DropPolicy
{
    /// <summary>
    /// 判定拖放动作。
    /// </summary>
    /// <param name="controlPressed">是否按下 Ctrl。</param>
    /// <param name="shiftPressed">是否按下 Shift。</param>
    /// <param name="sourcePath">被拖拽条目的路径（用于判断卷）。</param>
    /// <param name="targetDirectory">目标目录。</param>
    public static DropAction Resolve(
        bool controlPressed,
        bool shiftPressed,
        string sourcePath,
        string targetDirectory)
    {
        // 修饰键优先于卷判断
        if (controlPressed)
        {
            return DropAction.Copy;
        }

        if (shiftPressed)
        {
            return DropAction.Move;
        }

        return SameVolume(sourcePath, targetDirectory) ? DropAction.Move : DropAction.Copy;
    }

    /// <summary>
    /// 判断两个路径是否位于同一个卷。
    /// </summary>
    /// <remarks>
    /// 比较的是路径根（<c>C:\</c>、<c>D:\</c>、<c>\\server\share\</c>）。
    /// 网络路径的根是 UNC 共享，因此跨共享会被判为不同卷 —— 这也是资源管理器的行为。
    /// </remarks>
    public static bool SameVolume(string firstPath, string secondPath)
    {
        if (string.IsNullOrWhiteSpace(firstPath) || string.IsNullOrWhiteSpace(secondPath))
        {
            return false;
        }

        string? firstRoot = TryGetRoot(firstPath);
        string? secondRoot = TryGetRoot(secondPath);

        if (firstRoot is null || secondRoot is null)
        {
            return false;
        }

        return string.Equals(firstRoot, secondRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetRoot(string path)
    {
        try
        {
            return Path.GetPathRoot(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
