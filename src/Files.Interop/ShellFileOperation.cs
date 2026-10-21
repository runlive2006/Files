using System.Runtime.InteropServices;

namespace Files.Interop;

/// <summary>
/// <c>SHFileOperationW</c> 的 P/Invoke 与常量。
/// </summary>
/// <remarks>
/// 为什么 M1 就用 Shell 而不是 <see cref="File.Copy(string, string)"/>：
/// <list type="bullet">
/// <item>删除进回收站必须走 Shell（<c>FOF_ALLOWUNDO</c>），托管 API 做不到；</item>
/// <item>Shell 的复制引擎会正确处理 ACL、替代数据流、符号链接与跨卷降级；</item>
/// <item>重名冲突由系统提供"替换/跳过"对话框，不必自研。</item>
/// </list>
/// <para>
/// 已知限制（M3 换 <c>IFileOperation</c> 时解决）：
/// 不支持超过 MAX_PATH 的路径；进度与冲突使用系统自带界面，
/// 无法满足 PRD DLG-01/DLG-02 的"聚合式自定义对话框"要求。
/// </para>
/// </remarks>
internal static class ShellFileOperation
{
    internal const uint FoMove = 0x0001;
    internal const uint FoCopy = 0x0002;
    internal const uint FoDelete = 0x0003;
    internal const uint FoRename = 0x0004;

    /// <summary>允许撤销（删除时移入回收站）。</summary>
    internal const ushort FofAllowUndo = 0x0040;

    /// <summary>不显示确认对话框（确认由我们自己的 UI 负责）。</summary>
    internal const ushort FofNoConfirmation = 0x0010;

    /// <summary>不显示错误对话框（错误由我们自己的状态栏/日志呈现）。</summary>
    internal const ushort FofNoErrorUi = 0x0400;

    /// <summary>不提示"是否创建目录"。</summary>
    internal const ushort FofNoConfirmMkDir = 0x0200;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false, EntryPoint = "SHFileOperationW")]
    internal static extern int SHFileOperation(ref ShFileOpStruct lpFileOp);

    /// <summary>
    /// 执行一次文件操作。
    /// </summary>
    /// <param name="function">FO_* 操作码。</param>
    /// <param name="from">源路径集合。</param>
    /// <param name="to">目标目录；删除/重命名时为 <see langword="null"/>。</param>
    /// <param name="flags">FOF_* 标志。</param>
    /// <param name="ownerWindow">承载系统对话框的窗口句柄，可为 0。</param>
    /// <returns>0 表示成功；非 0 为 DE_* 错误码。</returns>
    internal static int Execute(uint function, IReadOnlyList<string> from, string? to, ushort flags, IntPtr ownerWindow)
    {
        ArgumentNullException.ThrowIfNull(from);
        if (from.Count == 0)
        {
            return 0;
        }

        // pFrom 是"双 null 结尾、内部以单个 null 分隔"的多字符串。
        // 封送器（LPWStr）会再补一个结尾 null，因此这里只额外补一个。
        string fromList = string.Join('\0', from) + "\0";

        var operation = new ShFileOpStruct
        {
            hwnd = ownerWindow,
            wFunc = function,
            pFrom = fromList,
            pTo = to,
            fFlags = flags,
            fAnyOperationsAborted = false,
            hNameMappings = IntPtr.Zero,
            lpszProgressTitle = null,
        };

        return SHFileOperation(ref operation);
    }

    /// <summary>把 DE_* 错误码翻译成可读中文。</summary>
    internal static string DescribeError(int code) => code switch
    {
        0 => "成功",
        2 => "找不到文件或文件夹",
        5 => "拒绝访问",
        0x71 => "源与目标为同一文件",
        0x72 => "多个源指向同一目标",
        0x74 => "不能对根目录执行此操作",
        0x75 => "不能把目标设为根目录",
        0x78 => "拒绝访问源文件",
        0x7C => "文件名无效",
        0x7E => "目标位置已存在同名文件夹",
        0x80 => "目标位置已存在同名文件",
        0x84 => "无法把源项放入回收站",
        0x85 => "无法把目标项放入回收站",
        0x88 => "目标文件是目录",
        0x89 => "源文件是目录",
        0x10000 => "操作被用户取消",
        _ => $"系统错误 0x{code:X}",
    };
}

/// <summary>
/// <c>SHFILEOPSTRUCTW</c> 的托管映射。
/// </summary>
/// <remarks>
/// <c>fFlags</c> 必须是 <see langword="ushort"/>（原生 <c>WORD</c>）。
/// 用 <see langword="int"/> 会让结构体整体错位，表现为操作随机失败。
/// </remarks>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct ShFileOpStruct
{
    internal IntPtr hwnd;
    internal uint wFunc;
    internal string pFrom;
    internal string? pTo;
    internal ushort fFlags;

    [MarshalAs(UnmanagedType.Bool)]
    internal bool fAnyOperationsAborted;

    internal IntPtr hNameMappings;
    internal string? lpszProgressTitle;
}
