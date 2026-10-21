using System.Runtime.InteropServices;

namespace Files.Interop;

/// <summary>
/// 从系统图标库取文件/文件夹图标。
/// </summary>
/// <remarks>
/// 关键设计：用 <c>SHGFI_USEFILEATTRIBUTES</c> 按<b>扩展名</b>取图标，
/// 而不是按完整路径。这样：
/// <list type="bullet">
/// <item>不产生任何磁盘访问 —— 10 万项目录下这是决定性的；</item>
/// <item>结果可按扩展名缓存，命中率极高（几百个扩展名覆盖绝大多数文件）。</item>
/// </list>
/// 代价是 <c>.exe</c>、<c>.lnk</c> 这类"每文件图标不同"的项会退化为通用图标。
/// 属于已知取舍，后续如需精确可按路径单独取。
/// </remarks>
public static class ShellIcon
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiUseFileAttributes = 0x000000010;

    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;

    /// <summary>
    /// 取一个图标句柄。调用方负责用 <see cref="DestroyIcon"/> 释放。
    /// </summary>
    /// <param name="extension">扩展名（含点），文件用；目录可传 <see langword="null"/>。</param>
    /// <param name="isDirectory">是否为目录。</param>
    /// <param name="large">是否取 32×32（否则 16×16）。</param>
    /// <returns>图标句柄；失败时返回 <see cref="IntPtr.Zero"/>。</returns>
    public static IntPtr GetIconHandle(string? extension, bool isDirectory, bool large)
    {
        // 用假文件名 + USEFILEATTRIBUTES：系统只看扩展名/属性，不碰磁盘
        string fakePath;
        uint attributes;

        if (isDirectory)
        {
            fakePath = "folder";
            attributes = FileAttributeDirectory;
        }
        else
        {
            string ext = string.IsNullOrWhiteSpace(extension) ? string.Empty : extension;
            fakePath = "file" + ext;
            attributes = FileAttributeNormal;
        }

        var info = new ShFileInfo();
        uint flags = ShgfiIcon | ShgfiUseFileAttributes | (large ? ShgfiLargeIcon : ShgfiSmallIcon);

        IntPtr result = SHGetFileInfo(
            fakePath,
            attributes,
            ref info,
            (uint)Marshal.SizeOf<ShFileInfo>(),
            flags);

        return result == IntPtr.Zero ? IntPtr.Zero : info.hIcon;
    }

    /// <summary>释放图标句柄。</summary>
    [DllImport("user32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false, EntryPoint = "SHGetFileInfoW")]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref ShFileInfo psfi,
        uint cbFileInfo,
        uint uFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        internal IntPtr hIcon;
        internal int iIcon;
        internal uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        internal string szTypeName;
    }
}
