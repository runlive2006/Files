namespace Files.Core.Diagnostics;

/// <summary>
/// 把当前进程的临时目录固定到指定位置。
/// </summary>
/// <remarks>
/// WPF 与 .NET 的若干组件都通过 <see cref="Path.GetTempPath"/> 落盘临时文件。
/// 通过改写 <c>TEMP</c>/<c>TMP</c> 环境变量（仅影响当前进程，不修改用户环境），
/// 可以把这些写入全部收拢到程序自己的目录内。
/// </remarks>
public static class ProcessTemp
{
    /// <summary>把临时目录指向 <paramref name="directoryPath"/>。</summary>
    /// <returns>实际生效的目录。</returns>
    public static string Use(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        Directory.CreateDirectory(directoryPath);

        Environment.SetEnvironmentVariable("TEMP", directoryPath);
        Environment.SetEnvironmentVariable("TMP", directoryPath);

        return directoryPath;
    }

    /// <summary>当前进程实际使用的临时目录。</summary>
    public static string Current => Path.GetTempPath();
}
