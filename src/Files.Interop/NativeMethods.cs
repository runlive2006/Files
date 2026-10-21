using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Files.Interop;

/// <summary>
/// 目录枚举用到的 Win32 常量与 P/Invoke 声明。
/// </summary>
internal static class NativeMethods
{
    /// <summary>无效句柄值。</summary>
    internal static readonly IntPtr InvalidHandleValue = new(-1);

    internal const int ErrorFileNotFound = 2;
    internal const int ErrorPathNotFound = 3;
    internal const int ErrorNoMoreFiles = 18;
    internal const int ErrorAccessDenied = 5;

    /// <summary>使用基本信息级别（跳过 8.3 短名，枚举更快）。</summary>
    internal const int FindExInfoBasic = 1;

    /// <summary>使用标准信息级别（含短名）。</summary>
    internal const int FindExInfoStandard = 0;

    /// <summary>只匹配目录。</summary>
    internal const int FindExSearchLimitToDirectories = 1;

    /// <summary>批量读取，减少内核往返（Win7+）。</summary>
    internal const uint FindFirstExLargeFetch = 0x00000002;

    internal const uint FileAttributeDirectory = 0x00000010;
    internal const uint FileAttributeReparsePoint = 0x00000400;
    internal const uint FileAttributeDevice = 0x00000040;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindFirstFileExW")]
    internal static extern SafeFindHandle FindFirstFileEx(
        string lpFileName,
        int fInfoLevelId,
        out Win32FindData lpFindFileData,
        int fSearchOp,
        IntPtr lpSearchFilter,
        uint dwAdditionalFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindNextFileW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FindNextFile(SafeFindHandle hFindFile, out Win32FindData lpFindFileData);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "FindClose")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FindClose(IntPtr hFindFile);
}

/// <summary>
/// <c>WIN32_FIND_DATAW</c> 的托管映射。
/// </summary>
/// <remarks>
/// 固定长度字符串缓冲区必须显式声明，否则结构体大小与原生不一致，
/// 会导致后续字段错位（典型症状是文件名乱码或大小为 0）。
/// <para>
/// <c>Pack = 4</c> 是必须的：原生的 <c>FILETIME</c> 是 4 字节对齐的两个 DWORD，
/// 而托管侧用 <c>long</c> 表示时默认按 8 字节对齐，会在 <c>dwFileAttributes</c>
/// 之后插入 4 字节填充，导致 <c>cFileName</c> 偏移 4 字节 —— 表现为文件名丢掉前两个字符。
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
internal struct Win32FindData
{
    internal uint FileAttributes;
    internal long CreationTime;
    internal long LastAccessTime;
    internal long LastWriteTime;
    internal uint FileSizeHigh;
    internal uint FileSizeLow;
    internal uint Reserved0;
    internal uint Reserved1;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    internal string FileName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
    internal string AlternateFileName;

    /// <summary>组合高低位得到 64 位文件大小。</summary>
    internal readonly long Size => ((long)FileSizeHigh << 32) | FileSizeLow;

    /// <summary>最后写入时间（UTC）。</summary>
    internal readonly DateTimeOffset LastWriteUtc => DateTimeOffset.FromFileTime(LastWriteTime).ToUniversalTime();
}
