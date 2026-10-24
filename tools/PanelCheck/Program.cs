// 验证 VirtualizingWrapPanel 是否真的虚拟化。
// 关键指标：10 万项时实际生成的容器数量必须有上界（而不是 10 万）。
// 用法：dotnet run --project tools/PanelCheck -c Release

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Files.App.Views;

namespace PanelCheck;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        int failures = 0;

        void Check(string label, bool ok, string detail = "")
        {
            Console.WriteLine(ok ? $"  [OK]   {label}" : $"  [FAIL] {label}  {detail}");

            if (!ok)
            {
                failures++;
            }
        }

        // WPF 必须跑在 STA 线程且有 Application 实例
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        app.Startup += (_, _) =>
        {
            try
            {
                Console.WriteLine("=== 100,000 项目录：虚拟化验证 ===");

                var items = new List<string>(100_000);

                for (int i = 0; i < 100_000; i++)
                {
                    items.Add($"item-{i:D6}.png");
                }

                var listBox = new ListBox
                {
                    ItemsSource = items,
                    Width = 1000,
                    Height = 700,

                    // 关键：CanContentScroll=true 才会使用面板的 IScrollInfo
                    // （按项滚动），否则 ScrollViewer 按像素滚，虚拟化不生效
                    ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingWrapPanel))),
                };

                ScrollViewer.SetCanContentScroll(listBox, true);

                var panel = FindPanel<VirtualizingWrapPanel>(listBox);

                // 强制布局（需要真实渲染上下文）
                var window = new Window
                {
                    Width = 1020,
                    Height = 740,
                    Content = listBox,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    Left = -32000,
                    Top = -32000,
                };

                window.Show();

                // 布局可能分多轮完成（面板注入 → 测量 → 生成容器），
                // 单次 UpdateLayout 不足，必须循环到容器出现或超时
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    listBox.UpdateLayout();
                    DoEvents();

                    if (panel?.InternalChildrenCount > 0)
                    {
                        break;
                    }

                    System.Threading.Thread.Sleep(20);
                }

                panel ??= FindPanel<VirtualizingWrapPanel>(listBox);

                if (panel is null)
                {
                    Check("找到 VirtualizingWrapPanel", false, "面板未实例化");
                    Fail();
                    return;
                }

                int realized = panel.InternalChildrenCount;
                Console.WriteLine($"  测量诊断：{panel.LastMeasureDiagnostics}");
                Console.WriteLine($"  实际容器数 = {realized}（总项数 100,000）");

                // 1000x700 视口 / (128x104 单元) ≈ 8 列 x 7 行 = 56，加缓冲
                Check("容器数远小于总项数", realized < 200, $"realized={realized}");
                Check("容器数足以填满视口", realized > 5, $"realized={realized}");

                // 滚动到底，验证偏移生效且容器数仍受控
                if (panel.ScrollOwner is not null)
                {
                    panel.SetVerticalOffset(panel.ExtentHeight);
                    listBox.UpdateLayout();
                    DoEvents();

                    int afterScroll = panel.InternalChildrenCount;
                    Console.WriteLine($"  滚到底后容器数 = {afterScroll}");
                    Console.WriteLine($"  偏移 = {panel.VerticalOffset:F0} / 可滚动高度 = {panel.ExtentHeight:F0}");

                    Check("滚动后容器数仍受控", afterScroll < 300, $"afterScroll={afterScroll}");
                    Check("滚动偏移已生效", panel.VerticalOffset > 0, $"offset={panel.VerticalOffset}");
                }
                else
                {
                    Check("ScrollOwner 已注入", false);
                }

                window.Close();
                DoEvents();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [FAIL] 异常：{ex}");
                failures++;
            }
            finally
            {
                app.Shutdown();
            }
        };

        app.Run();

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "RESULT=ALL_PASSED" : $"RESULT=FAILURES:{failures}");
        return failures == 0 ? 0 : 1;

        void Fail() => app.Shutdown();
    }

    private static T? FindPanel<T>(DependencyObject root) where T : Panel
    {
        if (root is T match)
        {
            return match;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);

        for (int i = 0; i < count; i++)
        {
            T? found = FindPanel<T>(VisualTreeHelper.GetChild(root, i));

            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static void DoEvents()
    {
        var frame = new DispatcherFrame();

        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new DispatcherOperationCallback(f => { ((DispatcherFrame)f!).Continue = false; return null; }),
            frame);

        Dispatcher.PushFrame(frame);
    }
}
