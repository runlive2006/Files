using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Files.Core.Models;
using Files.Interop;

namespace Files.App.ViewModels;

/// <summary>
/// 文件图标提供者：按需从系统图标库取图标并缓存。
/// </summary>
/// <remarks>
/// 缓存键是"扩展名 + 尺寸"，不是完整路径 —— 因为图标本身按扩展名取
/// （见 <see cref="ShellIcon"/>），所以几百个键就能覆盖十万项。
/// 这样既避免每项一次系统调用，也避免缓存无限增长。
/// </remarks>
public sealed class IconProvider
{
    private const int MaxCacheEntries = 512;

    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _useSystemIcons;

    /// <summary>创建图标提供者。</summary>
    public IconProvider(bool useSystemIcons) => _useSystemIcons = useSystemIcons;

    /// <summary>是否启用系统图标。</summary>
    /// <summary>是否启用系统图标。</summary>
    public bool UseSystemIcons => _useSystemIcons;

    /// <summary>
    /// 取条目的图标；未启用系统图标或取不到时返回 <see langword="null"/>
    /// （界面回退为按类型着色的方块）。
    /// </summary>
    /// <summary>取条目的图标。</summary>
    public ImageSource? GetIcon(FileSystemEntry entry, bool large)
    {
        if (!_useSystemIcons)
        {
            return null;
        }

        string key = BuildKey(entry, large);
        if (_cache.TryGetValue(key, out ImageSource? cached))
        {
            return cached;
        }

        ImageSource? icon = LoadIcon(entry, large);

        // 简单容量控制：满了就整体清空。图标总量很小，重建成本可忽略，
        // 且比实现 LRU 更不容易出错。
        if (_cache.Count >= MaxCacheEntries)
        {
            _cache.Clear();
        }

        _cache[key] = icon;
        return icon;
    }

    private static string BuildKey(FileSystemEntry entry, bool large)
    {
        string kind = entry.IsDirectory ? "<dir>" : entry.Extension.ToLowerInvariant();
        return (large ? "L|" : "S|") + kind;
    }

    private static ImageSource? LoadIcon(FileSystemEntry entry, bool large)
    {
        IntPtr handle = IntPtr.Zero;

        try
        {
            handle = ShellIcon.GetIconHandle(entry.Extension, entry.IsDirectory, large);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                ShellIcon.DestroyIcon(handle);
            }
        }
    }
}
