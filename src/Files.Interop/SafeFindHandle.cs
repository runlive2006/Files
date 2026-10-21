using Microsoft.Win32.SafeHandles;

namespace Files.Interop;

/// <summary>
/// 查找句柄的安全包装。析构或显式释放时自动调用 <c>FindClose</c>，
/// 避免枚举中断（异常、提前 return）时泄漏句柄。
/// </summary>
internal sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeFindHandle()
        : base(ownsHandle: true)
    {
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle() => NativeMethods.FindClose(handle);
}
