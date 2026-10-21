namespace Files.Core.Diagnostics;

/// <summary>
/// 确保进程有一个可写的临时目录。
/// </summary>
/// <remarks>
/// WPF 会在渲染 <c>GridViewColumnHeader</c> 时把光标资源落成临时文件
/// （<c>%TEMP%\WPF\*.lek</c>）。如果该目录不可写，异常会从
/// <c>Cursor.LoadFromStream</c> 一路抛出并直接终止进程 —— 表现为"双击没反应"。
/// <para>
/// 因此在窗口创建<b>之前</b>探测一次，必要时把 TEMP/TMP 指向应用自己的目录。
/// 这类环境（受限令牌、被策略锁定的临时目录、漫游配置异常）在企业环境并不罕见。
/// </para>
/// </remarks>
public static class TempDirectory
{
    private const string WpfSubFolder = "WPF";

    /// <summary>
    /// 返回可用的临时目录；若系统临时目录不可写，则切换到应用私有目录并返回它。
    /// </summary>
    /// <param name="probeFileName">探测用的文件名，仅用于日志。</param>
    public static string EnsureWritable(out string? fallbackReason)
    {
        fallbackReason = null;

        string systemTemp = Path.GetTempPath();

        // 必须同时探测系统临时目录和 WPF 的光标临时子目录：
        // 曾经出现过根目录可写、但 %TEMP%\WPF 不可写导致启动即崩的情况。
        string? rootError = null;
        string? wpfError = null;

        bool rootOk = CanWrite(systemTemp, out rootError);
        bool wpfOk = rootOk && CanWrite(Path.Combine(systemTemp, WpfSubFolder), out wpfError);

        if (rootOk && wpfOk)
        {
            return systemTemp;
        }

        fallbackReason = rootError ?? wpfError ?? "系统临时目录不可写";

        string fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Files",
            "temp");

        try
        {
            Directory.CreateDirectory(fallback);
            Directory.CreateDirectory(Path.Combine(fallback, WpfSubFolder));

            // 只影响当前进程，不修改用户环境变量
            Environment.SetEnvironmentVariable("TEMP", fallback);
            Environment.SetEnvironmentVariable("TMP", fallback);

            return fallback;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 连私有目录都建不出来时只能放弃，让上层决定如何提示
            fallbackReason = $"{fallbackReason}；回退目录也失败：{ex.Message}";
            return systemTemp;
        }
    }

    private static bool CanWrite(string directoryPath, out string? error)
    {
        error = null;

        try
        {
            Directory.CreateDirectory(directoryPath);

            string probe = Path.Combine(
                directoryPath,
                $".files-write-probe-{Guid.NewGuid():N}.tmp");

            using (File.Create(probe, bufferSize: 1, FileOptions.DeleteOnClose))
            {
                // 能创建即视为可写
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = $"{directoryPath}：{ex.Message}";
            return false;
        }
    }
}
