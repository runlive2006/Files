using System.Runtime.InteropServices;

namespace Files.Interop;

/// <summary>
/// 调用 Windows 原生「属性」对话框。
/// </summary>
/// <remarks>
/// <b>为什么直接调系统对话框而不是自绘</b>：
/// <list type="bullet">
/// <item>属性对话框包含"常规/安全/详细信息/以前的版本"等多页，其中「安全」页是
/// ACL 编辑器，自绘等同于重写整个权限模型；</item>
/// <item>系统对话框与 Shell 行为天然一致（共享、压缩、加密等属性都在）；</item>
/// <item>第三方 Shell 扩展也会在属性页里挂载自己的页签，自绘无法呈现。</item>
/// </list>
/// 因此这里只负责"把系统的属性对话框调起来"，不复制它的功能。
/// <para>
/// 使用 <c>SHObjectProperties</c>：它同时支持文件系统路径与 Shell 命名空间路径
/// （后者用于回收站、此电脑等虚拟位置）。
/// </para>
/// </remarks>
public static class ShellPropertiesDialog
{
    /// <summary>属性对话框类型：文件或文件夹。</summary>
    private const uint ShopFileProperties = 0x00000002;

    /// <summary>
    /// 为指定路径显示系统属性对话框。
    /// </summary>
    /// <param name="path">目标路径（文件系统路径或 Shell 命名空间路径）。</param>
    /// <param name="ownerWindow">父窗口句柄，可为 <see cref="IntPtr.Zero"/>。</param>
    /// <returns>成功返回 <see langword="true"/>。</returns>
    public static bool Show(string path, IntPtr ownerWindow)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return SHObjectProperties(ownerWindow, ShopFileProperties, path, null);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHObjectProperties(
        IntPtr hwnd,
        uint shopObjectType,
        [MarshalAs(UnmanagedType.LPWStr)] string pszObjectName,
        [MarshalAs(UnmanagedType.LPWStr)] string? pszPropertyPage);
}
