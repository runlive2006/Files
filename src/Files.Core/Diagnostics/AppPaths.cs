namespace Files.Core.Diagnostics;

/// <summary>
/// 应用的路径策略：所有可写数据都位于可执行文件所在目录下的 <c>data</c> 子目录。
/// </summary>
/// <remarks>
/// 这是一条<b>强制约束</b>（产品要求）：
/// <list type="bullet">
/// <item>配置、状态、日志、临时文件一律不写入 <c>%APPDATA%</c> / <c>%LOCALAPPDATA%</c> / 系统临时目录；</item>
/// <item>整个应用是"绿色便携"的：拷走文件夹即完成迁移，删除文件夹即彻底清除。</item>
/// </list>
/// <para>
/// 为什么这条约束同时也修掉了一个崩溃：WPF 渲染 <c>GridViewColumnHeader</c> 时会把光标资源
/// 写入 <c>%TEMP%\WPF\*.lek</c>。在普通用户账户下，如果系统临时目录被策略限制为只有管理员可写，
/// 异常会从 <c>Cursor.LoadFromStream</c> 抛出并直接终止进程 —— 用户看到的就是"双击没反应"。
/// 把 TEMP 固定到程序自己的目录后，这个问题从根上消失。
/// </para>
/// </remarks>
public sealed class AppPaths
{
    /// <summary>数据根目录名。</summary>
    public const string DataFolderName = "data";

    /// <summary>当前进程使用的路径策略；由 <see cref="Initialize"/> 设置。</summary>
    public static AppPaths? Current { get; private set; }

    /// <summary>创建路径策略。</summary>
    /// <param name="baseDirectory">可执行文件所在目录。</param>
    public AppPaths(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        BaseDirectory = Path.GetFullPath(baseDirectory);
        DataDirectory = Path.Combine(BaseDirectory, DataFolderName);
        SettingsFile = Path.Combine(DataDirectory, "settings.json");
        LogDirectory = Path.Combine(DataDirectory, "logs");
        TempDirectory = Path.Combine(DataDirectory, "temp");
    }

    /// <summary>可执行文件所在目录。</summary>
    public string BaseDirectory { get; }

    /// <summary>数据根目录（<c>&lt;exe&gt;\data</c>）。</summary>
    public string DataDirectory { get; }

    /// <summary>设置文件路径。</summary>
    public string SettingsFile { get; }

    /// <summary>日志目录。</summary>
    public string LogDirectory { get; }

    /// <summary>临时文件目录。</summary>
    public string TempDirectory { get; }

    /// <summary>
    /// 以可执行文件所在目录初始化并设为 <see cref="Current"/>。
    /// </summary>
    public static AppPaths Initialize(string baseDirectory)
    {
        Current = new AppPaths(baseDirectory);
        return Current;
    }

    /// <summary>
    /// 确保数据目录存在且可写；不可写时抛出带可操作提示的异常。
    /// </summary>
    /// <remarks>
    /// 必须在任何界面元素创建之前调用。若程序被放在 <c>Program Files</c> 这类只读位置，
    /// 应当<b>明确报错并退出</b>，而不是在渲染阶段以未处理异常的形式崩溃。
    /// </remarks>
    public void EnsureWritable()
    {
        CreateAndProbe(DataDirectory);
        CreateAndProbe(LogDirectory);
        CreateAndProbe(TempDirectory);

        // WPF 会在临时目录下再建一个 WPF 子目录存放光标资源，必须一并探测
        CreateAndProbe(Path.Combine(TempDirectory, "WPF"));
    }

    private static void CreateAndProbe(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);

            string probe = Path.Combine(directoryPath, $".write-probe-{Guid.NewGuid():N}.tmp");
            using (File.Create(probe, bufferSize: 1, FileOptions.DeleteOnClose))
            {
                // 能创建即视为可写
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"程序目录不可写：{directoryPath}",
                ex);
        }
    }
}
