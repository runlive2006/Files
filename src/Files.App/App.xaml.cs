using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Files.Core.Diagnostics;

namespace Files.App;

/// <summary>
/// 应用入口。
/// </summary>
/// <remarks>
/// 启动顺序有硬性要求：
/// <list type="number">
/// <item>安装全局异常处理（越早越好，否则早期异常无从记录）；</item>
/// <item>确定数据目录并确认可写；</item>
/// <item>把进程临时目录固定到程序目录内；</item>
/// <item>最后才创建界面 —— WPF 一旦开始渲染就会往临时目录写光标资源。</item>
/// </list>
/// </remarks>
public partial class App : Application
{
    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        // 进程创建 → 进入托管入口 的耗时，是冷启动预算里"运行时自身"那一部分
        StartupProbe.Mark("app-startup");

        InstallCrashHandlers();

        AppPaths paths = AppPaths.Initialize(AppContext.BaseDirectory);

        try
        {
            paths.EnsureWritable();
        }
        catch (Exception ex)
        {
            // 明确的失败胜过渲染阶段的未处理异常
            MessageBox.Show(
                $"""
                 无法在程序所在目录写入数据。

                 目录：{paths.DataDirectory}

                 原因：{ex.Message}

                 请把程序放到你有写入权限的文件夹中（例如 D:\Tools\Files），
                 不要放在 C:\Program Files 下。
                 """,
                "Files 无法启动",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(2);
            return;
        }

        // 必须在任何 WPF 视觉元素创建之前：WPF 会把光标资源写到 %TEMP%\WPF
        string temp = ProcessTemp.Use(paths.TempDirectory);
        Debug.WriteLine($"[paths] data={paths.DataDirectory} temp={temp}");

        base.OnStartup(e);

        StartupProbe.Mark("app-ready");
    }

    /// <summary>
    /// 安装全局异常处理，避免"双击后什么都没发生"这种无从排查的失败。
    /// </summary>
    private void InstallCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            ReportCrash(args.Exception, "UI 线程");
            args.Handled = true;
            Shutdown(1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                ReportCrash(exception, "非 UI 线程");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ReportCrash(args.Exception, "未观察的任务");
            args.SetObserved();
        };
    }

    /// <summary>写崩溃日志并提示用户。</summary>
    private static void ReportCrash(Exception exception, string source)
    {
        string? logPath = null;

        try
        {
            // 数据目录可能尚未初始化（极早期异常），因此退回 exe 目录下的 data\logs
            string logDirectory = AppPaths.Current?.LogDirectory
                ?? Path.Combine(AppContext.BaseDirectory, AppPaths.DataFolderName, "logs");

            Directory.CreateDirectory(logDirectory);
            logPath = Path.Combine(logDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            var builder = new StringBuilder();
            builder.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"来源：{source}");
            builder.AppendLine($"版本：{typeof(App).Assembly.GetName().Version}");
            builder.AppendLine($"临时目录：{ProcessTemp.Current}");
            builder.AppendLine(new string('-', 60));
            builder.AppendLine(exception.ToString());

            File.WriteAllText(logPath, builder.ToString(), Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logPath = null;
        }

        try
        {
            MessageBox.Show(
                logPath is null
                    ? $"发生未处理的错误：\n\n{exception.Message}"
                    : $"发生未处理的错误，日志已保存到：\n{logPath}\n\n{exception.Message}",
                "Files 遇到错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // 连提示框都弹不出来时只能静默退出
        }
    }
}
