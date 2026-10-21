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
public partial class App : Application
{
    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        // 进程创建 → 进入托管入口 的耗时，是冷启动预算里"运行时自身"那一部分
        StartupProbe.Mark("app-startup");

        InstallCrashHandlers();

        // 必须在任何 WPF 视觉元素创建之前完成：WPF 会把光标资源写到 %TEMP%\WPF
        string temp = TempDirectory.EnsureWritable(out string? fallbackReason);
        Debug.WriteLine($"[temp] 使用 {temp}" + (fallbackReason is null ? string.Empty : $"（回退原因：{fallbackReason}）"));

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
            string logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Files",
                "logs");

            Directory.CreateDirectory(logDirectory);
            logPath = Path.Combine(
                logDirectory,
                $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            var builder = new StringBuilder();
            builder.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"来源：{source}");
            builder.AppendLine($"版本：{typeof(App).Assembly.GetName().Version}");
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
