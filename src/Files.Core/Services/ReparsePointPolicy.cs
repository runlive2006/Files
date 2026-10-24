namespace Files.Core.Services;

/// <summary>
/// 判断一个重解析点是否为「可安全进入的云占位符」。
/// </summary>
/// <remarks>
/// <b>问题背景</b>：云盘（WPS 云盘、OneDrive 文件按需）的目录通常带
/// <c>ReparsePoint</c> 属性。而递归搜索为了防循环，默认<b>不跟随重解析点</b> ——
/// 直接沿用会导致云盘里的文件搜不到。
/// <para>
/// <b>区分办法</b>：真实链接（符号链接、junction）能解析出目标路径；
/// 云占位符的 <c>ReparsePoint</c> 是驱动层的挂载标记，<b>解析不出目标</b>。
/// 因此：
/// </para>
/// <list type="bullet">
/// <item>能解析出目标 → 真实链接 → 不跟随（防循环、防跨盘爆炸）；</item>
/// <item>解析不出目标 → 云占位符 → 可以进入（就是普通目录）。</item>
/// </list>
/// <para>
/// 该判定只在 Windows 上有意义；其他平台一律视为普通目录。
/// </para>
/// </remarks>
public static class ReparsePointPolicy
{
    /// <summary>
    /// 判断该目录是否应被当作普通目录进入。
    /// </summary>
    /// <param name="directoryPath">目录路径。</param>
    /// <param name="attributes">已读到的文件属性。</param>
    public static bool IsTraversable(string directoryPath, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) == 0)
        {
            return true;
        }

        // 重解析点：能解析出目标就是真实链接，不进入
        return !ResolvesToTarget(directoryPath);
    }

    /// <summary>
    /// 尝试解析链接目标；返回 <see langword="true"/> 表示是真实链接。
    /// </summary>
    private static bool ResolvesToTarget(string directoryPath)
    {
        try
        {
            FileSystemInfo? target = new DirectoryInfo(directoryPath).ResolveLinkTarget(returnFinalTarget: false);
            return target is not null;
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or ArgumentException
                                       or NotSupportedException)
        {
            // 解析失败按"非链接"处理：云占位符正是这种情况
            return false;
        }
    }
}
