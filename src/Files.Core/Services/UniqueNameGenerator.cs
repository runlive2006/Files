namespace Files.Core.Services;

/// <summary>
/// 在目录中生成不冲突的名称，行为与资源管理器的"新建文件夹"一致。
/// </summary>
/// <remarks>
/// 资源管理器不会在重名时弹错，而是自动追加序号：<c>新建文件夹</c>、
/// <c>新建文件夹 (2)</c>、<c>新建文件夹 (3)</c>……
/// </remarks>
public static class UniqueNameGenerator
{
    /// <summary>同名冲突时最多尝试的次数，防止死循环。</summary>
    private const int MaxAttempts = 10_000;

    /// <summary>
    /// 生成一个在 <paramref name="directoryPath"/> 下尚未被占用的名称。
    /// </summary>
    /// <param name="directoryPath">目标目录。</param>
    /// <param name="baseName">基名（不含序号，也不含扩展名）。</param>
    /// <param name="extension">扩展名，可含前导点，可为空。</param>
    /// <returns>可用的名称，例如 <c>新建文件夹 (3)</c>。</returns>
    public static string Generate(string directoryPath, string baseName, string extension = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);

        string suffix = string.IsNullOrEmpty(extension) || extension.StartsWith('.')
            ? extension
            : "." + extension;

        string candidate = baseName + suffix;
        if (!Exists(directoryPath, candidate))
        {
            return candidate;
        }

        for (int index = 2; index < MaxAttempts; index++)
        {
            candidate = $"{baseName} ({index}){suffix}";
            if (!Exists(directoryPath, candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"无法为 {baseName} 生成可用名称：尝试次数过多。");
    }

    /// <summary>
    /// 判断目标目录下是否已存在同名文件或文件夹。
    /// </summary>
    /// <remarks>
    /// 必须同时检查文件与目录：Windows 不允许同名文件与目录共存，
    /// 只查 <see cref="File.Exists"/> 会漏掉同名文件夹，导致新建时抛异常。
    /// </remarks>
    public static bool Exists(string directoryPath, string name)
    {
        string fullPath = Path.Combine(directoryPath, name);
        return File.Exists(fullPath) || Directory.Exists(fullPath);
    }
}
