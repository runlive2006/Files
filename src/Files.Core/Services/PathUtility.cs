using System.Text;

namespace Files.Core.Services;

/// <summary>
/// 路径处理的集中实现：环境变量展开、长路径前缀、保留设备名、Unicode 正规化。
/// </summary>
/// <remarks>
/// 路径是文件管理器最容易出数据事故的地方（PRD §5.4）。所有路径相关判断都必须走这里，
/// 不允许在别处零散地 <c>Path.Combine</c> + 字符串比较。
/// </remarks>
public static class PathUtility
{
    /// <summary>传统 MAX_PATH 限制。</summary>
    public const int MaxPath = 260;

    /// <summary>Win32 扩展路径前缀。</summary>
    public const string ExtendedPrefix = @"\\?\";

    /// <summary>UNC 在扩展路径下的前缀。</summary>
    public const string ExtendedUncPrefix = @"\\?\UNC\";

    private static readonly char[] InvalidFileNameChars =
        ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>
    /// 展开用户输入：去引号与首尾空白、展开 <c>%VAR%</c> 形式的环境变量。
    /// </summary>
    public static string Expand(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        string trimmed = input.Trim().Trim('"').Trim();
        return Environment.ExpandEnvironmentVariables(trimmed);
    }

    /// <summary>
    /// 判断名称是否为 Windows 保留设备名（<c>CON</c>、<c>NUL</c>、<c>COM1</c>…）。
    /// </summary>
    /// <remarks>
    /// 保留名带扩展名同样被系统拦截（<c>CON.txt</c>、<c>LPT9.tar.gz</c> 都是设备），
    /// 因此比较的是"第一个点之前的部分"。注意不能用
    /// <see cref="Path.GetFileNameWithoutExtension(string)"/> —— 它只去掉最后一个扩展名，
    /// 对 <c>LPT9.tar.gz</c> 会得到 <c>LPT9.tar</c> 从而漏判。
    /// </remarks>
    public static bool IsReservedDeviceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string trimmed = name.Trim();
        int firstDot = trimmed.IndexOf('.');
        string baseName = firstDot >= 0 ? trimmed[..firstDot] : trimmed;

        return ReservedDeviceNames.Contains(baseName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断名称是否为合法的单个文件/目录名（不含路径分隔符）。
    /// </summary>
    public static bool IsValidFileName(string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or "..")
        {
            return false;
        }

        // 尾随空格与点在 Win32 下会被静默截断，属于必须拒绝的输入
        if (name[^1] is ' ' or '.')
        {
            return false;
        }

        foreach (char c in name)
        {
            if (c < 32 || InvalidFileNameChars.Contains(c))
            {
                return false;
            }
        }

        return !IsReservedDeviceName(name);
    }

    /// <summary>
    /// 是否已经带扩展前缀。
    /// </summary>
    public static bool HasExtendedPrefix(string path) =>
        path.StartsWith(ExtendedPrefix, StringComparison.Ordinal);

    /// <summary>
    /// 是否需要加扩展前缀才能访问（用于超长路径）。
    /// </summary>
    public static bool NeedsExtendedPrefix(string path) =>
        !HasExtendedPrefix(path) && path.Length >= MaxPath - 12;

    /// <summary>
    /// 转换为 <c>\\?\</c> 扩展路径，以绕过 MAX_PATH。
    /// </summary>
    /// <remarks>
    /// 相对路径会先转为绝对路径。UNC 路径必须转成 <c>\\?\UNC\server\share</c>，
    /// 直接加前缀会得到无效的 <c>\\?\\\server\share</c>。
    /// </remarks>
    public static string ToExtendedPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (HasExtendedPrefix(path))
        {
            return path;
        }

        // 设备路径（\\.\）不参与前缀改写
        if (path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return path;
        }

        string absolute = Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(path);

        if (absolute.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // UNC：\\server\share\... -> \\?\UNC\server\share\...
            return ExtendedUncPrefix + absolute[2..];
        }

        return ExtendedPrefix + absolute;
    }

    /// <summary>
    /// 去掉 <c>\\?\</c> / <c>\\?\UNC\</c> 前缀，还原为常规路径。
    /// </summary>
    public static string FromExtendedPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.StartsWith(ExtendedUncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[ExtendedUncPrefix.Length..];
        }

        if (path.StartsWith(ExtendedPrefix, StringComparison.Ordinal))
        {
            return path[ExtendedPrefix.Length..];
        }

        return path;
    }

    /// <summary>
    /// 生成用于比较的规范化形式：统一分隔符、去尾分隔符、Unicode NFC、忽略大小写。
    /// </summary>
    public static string NormalizeForComparison(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string unified = path.Replace('/', '\\').TrimEnd('\\');
        string normalized = unified.Normalize(NormalizationForm.FormC);
        return normalized.ToUpperInvariant();
    }

    /// <summary>
    /// 判断两个路径是否指向同一位置（Windows 语义：不区分大小写，允许分隔符差异）。
    /// </summary>
    public static bool AreEquivalent(string left, string right) =>
        string.Equals(
            NormalizeForComparison(left),
            NormalizeForComparison(right),
            StringComparison.Ordinal);

    /// <summary>
    /// 确保路径以目录分隔符结尾（盘符根如 <c>C:</c> 会补成 <c>C:\</c>）。
    /// </summary>
    public static string EnsureTrailingSeparator(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (path.EndsWith('\\') || path.EndsWith('/'))
        {
            return path;
        }

        return path + '\\';
    }

    /// <summary>
    /// 拆分为路径段，忽略空段与根段。
    /// </summary>
    public static string[] SplitSegments(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
    }
}
