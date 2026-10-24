using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Files.Core.Models;

namespace Files.App.ViewModels;

/// <summary>
/// 列表行视图模型：显示所需的一切都在构造时算好。
/// </summary>
/// <remarks>
/// 刻意做成不可变 record 且<b>不</b>实现 <see cref="System.ComponentModel.INotifyPropertyChanged"/>：
/// 10 万项目录下，每行的事件订阅与可变状态会显著抬高内存占用
/// （参见 PERF-BASELINE 的每项预算）。
/// <para>
/// 图标提供两种形态：真实系统图标（<see cref="Icon"/>）或按类型着色的方块
/// （<see cref="IconBrush"/>）。为了让 XAML 不必写值转换器，可见性也在这里预先算好。
/// </para>
/// </remarks>
public sealed record EntryRow(
    string Name,
    string FullPath,
    bool IsDirectory,
    string Kind,
    string SizeText,
    string LastModifiedText,
    Brush IconBrush,
    ImageSource? Icon,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc)
{
    /// <summary>
    /// 返回显示名称。
    /// </summary>
    /// <remarks>
    /// 列表项的 UI Automation 名称在未显式设置时会退化为 <c>ToString()</c>。
    /// 默认的 record ToString 会输出全部字段，屏幕阅读器会念出一长串内部数据，
    /// 因此这里重写为只返回名称。
    /// </remarks>
    public override string ToString() => Name;

    /// <summary>真实图标的可见性。</summary>
    public Visibility IconVisibility => Icon is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>占位色块的可见性（与真实图标互斥）。</summary>
    public Visibility PlaceholderVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>从列举结果构造第 <paramref name="index"/> 行。</summary>
    /// <param name="listing">列举结果。</param>
    /// <param name="index">行下标。</param>
    /// <param name="icons">图标提供者；为 <see langword="null"/> 时使用占位色块。</param>
    /// <param name="largeIcon">是否使用大图标（缩略图视图）。</param>
    public static EntryRow From(
        DirectoryListing listing,
        int index,
        IconProvider? icons = null,
        bool largeIcon = false)
    {
        FileSystemEntry entry = listing.Entries[index];

        return new EntryRow(
            entry.Name,
            listing.GetFullPath(entry),
            entry.IsDirectory,
            DescribeKind(entry),
            entry.IsDirectory ? string.Empty : FormatSize(entry.SizeBytes),
            entry.LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture),
            IconBrushes.For(entry),
            icons?.GetIcon(entry, largeIcon),
            entry.SizeBytes,
            entry.LastModified);
    }

    private static string DescribeKind(FileSystemEntry entry)
    {
        if (entry.IsDirectory)
        {
            return "文件夹";
        }

        string extension = entry.Extension;
        return string.IsNullOrEmpty(extension) ? "文件" : extension;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
    };
}

/// <summary>
/// 占位图标着色：系统图标关闭或取不到时使用。
/// </summary>
internal static class IconBrushes
{
    private static readonly Brush Folder = Freeze(Color.FromRgb(0xE8, 0xB4, 0x3C));
    private static readonly Brush Image = Freeze(Color.FromRgb(0x4C, 0xA0, 0xE0));
    private static readonly Brush Code = Freeze(Color.FromRgb(0x6C, 0xC0, 0x6C));
    private static readonly Brush Document = Freeze(Color.FromRgb(0x9A, 0x9A, 0x9A));
    private static readonly Brush Other = Freeze(Color.FromRgb(0xB0, 0xB0, 0xB0));

    internal static Brush For(FileSystemEntry entry)
    {
        if (entry.IsDirectory)
        {
            return Folder;
        }

        return entry.Extension.ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => Image,
            ".cs" or ".js" or ".ts" or ".py" or ".cpp" or ".h" or ".json" or ".xml" => Code,
            ".txt" or ".md" or ".log" or ".pdf" or ".docx" or ".xlsx" => Document,
            _ => Other,
        };
    }

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
