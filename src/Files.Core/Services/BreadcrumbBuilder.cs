namespace Files.Core.Services;

/// <summary>
/// 面包屑的一个分段。
/// </summary>
/// <param name="DisplayName">显示文字（段名）。</param>
/// <param name="FullPath">点击后应导航到的完整路径。</param>
public readonly record struct BreadcrumbSegment(string DisplayName, string FullPath);

/// <summary>
/// 把路径拆成面包屑分段。
/// </summary>
/// <remarks>
/// 与资源管理器行为对齐的要点：
/// <list type="bullet">
/// <item>盘符根单独成段（<c>C:\</c> → 「Win11 (C:)」或「C:」）；</item>
/// <item>UNC 路径的第一段是 <c>\\server\share</c>，不能拆成两段；</item>
/// <item>每段都携带<b>累积到该段的完整路径</b>，点击即可导航；</item>
/// <item>超长路径由界面层决定省略哪几段，Core 只负责完整拆分。</item>
/// </list>
/// </remarks>
public static class BreadcrumbBuilder
{
    /// <summary>
    /// 拆分为面包屑分段。
    /// </summary>
    /// <param name="path">完整路径。</param>
    /// <param name="rootDisplayName">根分段的显示名（如卷标「Win11 (C:)」）；为空则用盘符。</param>
    public static IReadOnlyList<BreadcrumbSegment> Build(string? path, string? rootDisplayName = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        string normalized = path.TrimEnd('\\', '/');

        // 盘符根（C:）去掉分隔符后长度为 2，要补回 C:\
        if (normalized.Length == 2 && normalized[1] == ':')
        {
            normalized += "\\";
        }

        var segments = new List<BreadcrumbSegment>();

        // UNC：\\server\share\a\b
        if (normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return BuildUnc(normalized, segments);
        }

        // 盘符路径：C:\a\b
        if (normalized.Length >= 2 && normalized[1] == ':')
        {
            string drive = normalized[..2];
            string rootPath = drive + "\\";

            segments.Add(new BreadcrumbSegment(
                string.IsNullOrWhiteSpace(rootDisplayName) ? drive : rootDisplayName!,
                rootPath));

            string remainder = normalized.Length > 3 ? normalized[3..] : string.Empty;

            return AppendRelative(segments, remainder, rootPath);
        }

        // 其他（相对路径等）：整条作为一个分段
        segments.Add(new BreadcrumbSegment(normalized, normalized));
        return segments;
    }

    private static IReadOnlyList<BreadcrumbSegment> BuildUnc(string normalized, List<BreadcrumbSegment> segments)
    {
        // \\server\share 作为一段
        string[] parts = normalized[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            segments.Add(new BreadcrumbSegment(normalized, normalized));
            return segments;
        }

        string shareRoot = @"\\" + parts[0] + @"\" + parts[1];
        segments.Add(new BreadcrumbSegment(shareRoot, shareRoot));

        string remainder = parts.Length > 2 ? string.Join('\\', parts[2..]) : string.Empty;

        return AppendRelative(segments, remainder, shareRoot);
    }

    private static IReadOnlyList<BreadcrumbSegment> AppendRelative(
        List<BreadcrumbSegment> segments,
        string remainder,
        string basePath)
    {
        if (string.IsNullOrEmpty(remainder))
        {
            return segments;
        }

        string current = basePath;

        foreach (string part in remainder.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.EndsWith('\\')
                ? current + part
                : current + "\\" + part;

            segments.Add(new BreadcrumbSegment(part, current));
        }

        return segments;
    }

    /// <summary>
    /// 获取盘符根的显示名（含卷标，如 <c>Win11 (C:)</c>）；失败时回退为盘符。
    /// </summary>
    /// <remarks>
    /// 读卷标会访问磁盘；调用方应缓存结果，不要每次刷新界面都调用。
    /// </remarks>
    public static string GetRootDisplayName(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(path);

            if (string.IsNullOrEmpty(root))
            {
                return path;
            }

            var drive = new DriveInfo(root);

            if (!drive.IsReady || string.IsNullOrWhiteSpace(drive.VolumeLabel))
            {
                return drive.Name.TrimEnd('\\');
            }

            return $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }
}
