using System.Globalization;

namespace Files.Core.Services;

/// <summary>
/// 详情窗格要显示的内容。
/// </summary>
/// <param name="PrimaryText">主标题（名称，或「已选择 N 项」）。</param>
/// <param name="SecondaryText">副标题（类型与位置等）。</param>
/// <param name="DetailText">细节行（大小、时间、计数等）。</param>
/// <param name="IsPending">是否仍在计算中（如文件夹的子项数）。</param>
public readonly record struct DetailsPaneContent(
    string PrimaryText,
    string SecondaryText,
    string DetailText,
    bool IsPending)
{
    /// <summary>空状态。</summary>
    public static DetailsPaneContent Empty { get; } =
        new("未选择项目", string.Empty, string.Empty, false);
}

/// <summary>
/// 构造详情窗格文本。
/// </summary>
/// <remarks>
/// 把文案逻辑放在 Core 而不是 XAML 里，是为了能直接单元测试三种形态
/// （无选中 / 单选文件 / 单选文件夹 / 多选）的措辞与字段取舍，
/// 而不必启动界面。
/// </remarks>
public static class DetailsPaneFormatter
{
    /// <summary>未选中任何项目。</summary>
    /// <param name="currentDirectory">当前目录（用于显示目录汇总）。</param>
    /// <param name="summary">当前目录的直接子项统计；未知时传 <see langword="null"/>。</param>
    public static DetailsPaneContent NoSelection(string? currentDirectory, DirectorySummary? summary)
    {
        if (string.IsNullOrWhiteSpace(currentDirectory))
        {
            return DetailsPaneContent.Empty;
        }

        string location = currentDirectory;

        if (summary is null)
        {
            return new DetailsPaneContent(
                Path.GetFileName(currentDirectory.TrimEnd('\\')) is { Length: > 0 } name ? name : currentDirectory,
                location,
                string.Empty,
                IsPending: false);
        }

        DirectorySummary value = summary.Value;

        return new DetailsPaneContent(
            Path.GetFileName(currentDirectory.TrimEnd('\\')) is { Length: > 0 } dirName ? dirName : currentDirectory,
            location,
            value.Describe(),
            IsPending: false);
    }

    /// <summary>选中单个文件。</summary>
    public static DetailsPaneContent SingleFile(
        string fullPath,
        long sizeBytes,
        DateTimeOffset modifiedUtc,
        DateTimeOffset createdUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        string name = Path.GetFileName(fullPath);
        string extension = Path.GetExtension(fullPath);

        string type = string.IsNullOrEmpty(extension)
            ? "文件"
            : $"{extension.TrimStart('.').ToUpperInvariant()} 文件";

        string detail = string.Create(
            CultureInfo.CurrentCulture,
            $"{FormatSize(sizeBytes)}　修改：{FormatTime(modifiedUtc)}　创建：{FormatTime(createdUtc)}");

        return new DetailsPaneContent(name, $"{type}　{fullPath}", detail, IsPending: false);
    }

    /// <summary>
    /// 选中单个文件夹。
    /// </summary>
    /// <param name="fullPath">文件夹路径。</param>
    /// <param name="summary">子项统计；为 <see langword="null"/> 表示仍在计算。</param>
    /// <param name="modifiedUtc">修改时间。</param>
    public static DetailsPaneContent SingleFolder(
        string fullPath,
        DirectorySummary? summary,
        DateTimeOffset modifiedUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        string name = Path.GetFileName(fullPath.TrimEnd('\\'));

        if (string.IsNullOrEmpty(name))
        {
            // 盘符根（C:\）没有末段
            name = fullPath;
        }

        string detail = summary is null
            ? $"正在统计子项…　修改：{FormatTime(modifiedUtc)}"
            : $"{summary.Value.Describe()}　修改：{FormatTime(modifiedUtc)}";

        return new DetailsPaneContent(
            name,
            $"文件夹　{fullPath}",
            detail,
            IsPending: summary is null);
    }

    /// <summary>选中多个项目。</summary>
    /// <param name="count">选中数量。</param>
    /// <param name="totalSizeBytes">文件合计大小；未知时传 <see langword="null"/>。</param>
    /// <param name="folderCount">其中文件夹数量。</param>
    public static DetailsPaneContent Multiple(int count, long? totalSizeBytes, int folderCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count == 0)
        {
            return DetailsPaneContent.Empty;
        }

        int fileCount = count - folderCount;

        string composition = (folderCount, fileCount) switch
        {
            (0, _) => "全部为文件",
            (_, 0) => "全部为文件夹",
            _ => $"{folderCount} 个文件夹，{fileCount} 个文件",
        };

        string detail = totalSizeBytes is null or 0
            ? composition
            : $"{composition}　合计 {FormatSize(totalSizeBytes.Value)}";

        return new DetailsPaneContent($"已选择 {count} 项", composition, detail, IsPending: false);
    }

    /// <summary>格式化大小。</summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        < 0 => "未知",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
    };

    /// <summary>格式化时间（本地时区）。</summary>
    public static string FormatTime(DateTimeOffset utc) =>
        utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
}
