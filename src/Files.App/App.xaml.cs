using System.Windows;
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

        base.OnStartup(e);

        StartupProbe.Mark("app-ready");
    }
}
