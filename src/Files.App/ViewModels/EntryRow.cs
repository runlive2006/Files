using System.Globalization;
using System.Windows.Media;
using Files.Core.Models;

namespace Files.App.ViewModels;

/// <summary>
/// 列表行视图模型：显示所需的一切都在构造时算好。
/// </summary>
/// <remarks>
/// 刻意做成不可变 record 且<b>不</b>实现 <see cref="System.ComponentModel.INotifyPropertyChanged"/>：
/// 10 万项目录下，每行的事件订阅与可变状态会显著抬高内存占用
/// （参见 PERF-BASELINE 的每项预算）。需要编辑某一行时再引入"仅活动行可变"的机制。
/// </remarks>
public sealed record EntryRow(
    string Name,
    string FullPath,
    bool IsDirectory,
    string Kind,
    string SizeText,
    string LastModifiedText,
    Brush IconBrush)
{
    /// <summary>从列举结果构造第 <paramref name="index"/> 行。</summary>
    public static EntryRow From(DirectoryListing listing, int index)
    {
        FileSystemEntry entry = listing.Entries[index];

        return new EntryRow(
            entry.Name,
            listing.GetFullPath(entry),
            entry.IsDirectory,
            DescribeKind(entry),
            entry.IsDirectory ? string.Empty : FormatSize(entry.SizeBytes),
            entry.LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture),
            IconBrushes.For(entry));
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
/// 占位的图标着色。真实 Shell 图标（<c>SHGetFileInfo</c> / <c>IImageList</c>）
/// 属于 M3 任务 3.5，这里先用按类型着色的方块，避免过早引入逐项图标开销。
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
