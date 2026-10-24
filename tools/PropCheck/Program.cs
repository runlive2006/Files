global using PropCheck;
// 验证 ShellPropertiesDialog 能否真正调起系统属性对话框。
// 思路：调用后枚举该进程的顶层窗口，找类名为 #32770（对话框）且标题含"属性"的窗口。
// 这样即使不弹给人看，也能确认"对话框确实被创建了"。

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Files.Interop;

var failures = 0;

void Check(string label, bool ok, string detail = "")
{
    Console.WriteLine(ok ? $"  [OK]   {label}" : $"  [FAIL] {label}  {detail}");

    if (!ok)
    {
        failures++;
    }
}

// 遵守项目数据规范：不写系统 TEMP（本机 TEMP 指向无写权限的目录），
// 统一放在程序目录下的 data\ 里
string baseDir = Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(baseDir);
string tempDir = Path.Combine(baseDir, "PropCheck_" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(tempDir);
string testFile = Path.Combine(tempDir, "sample.txt");
File.WriteAllText(testFile, "hello");

try
{
    Console.WriteLine("=== 属性对话框验证 ===");

    // 空路径应返回 false，不应抛异常
    Check("空路径返回 false", !ShellPropertiesDialog.Show("  ", IntPtr.Zero));

    // 真实文件：调用后应在 2 秒内出现标题含"属性"的对话框窗口
    Console.WriteLine($"  调用 SHObjectProperties(\"{testFile}\") ...");

    bool shown = ShellPropertiesDialog.Show(testFile, IntPtr.Zero);
    Check("调用返回 true", shown);

    if (shown)
    {
        // 等待对话框出现，最多 3 秒
        IntPtr dialog = IntPtr.Zero;

        for (int i = 0; i < 30; i++)
        {
            dialog = FindPropertiesDialog();

            if (dialog != IntPtr.Zero)
            {
                break;
            }

            Thread.Sleep(100);
        }

        Check("找到属性对话框窗口", dialog != IntPtr.Zero,
            dialog == IntPtr.Zero ? "未在 3 秒内出现" : "");

        if (dialog != IntPtr.Zero)
        {
            var title = new StringBuilder(256);
            _ = NativeWindows.GetWindowText(dialog, title, title.Capacity);
            var className = new StringBuilder(256);
            _ = NativeWindows.GetClassName(dialog, className, className.Capacity);

            Console.WriteLine($"  对话框标题=\"{title}\"  类名=\"{className}\"");

            // 关闭对话框，避免残留
            _ = NativeWindows.PostMessage(dialog, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(500);
        }
    }

    // 目录也应能打开属性
    bool dirShown = ShellPropertiesDialog.Show(tempDir, IntPtr.Zero);
    Check("目录属性调用返回 true", dirShown);

    if (dirShown)
    {
        IntPtr dialog = IntPtr.Zero;

        for (int i = 0; i < 30; i++)
        {
            dialog = FindPropertiesDialog();

            if (dialog != IntPtr.Zero)
            {
                break;
            }

            Thread.Sleep(100);
        }

        Check("目录属性对话框出现", dialog != IntPtr.Zero);

        if (dialog != IntPtr.Zero)
        {
            _ = NativeWindows.PostMessage(dialog, 0x0010, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(500);
        }
    }
}
finally
{
    try
    {
        Directory.Delete(tempDir, recursive: true);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
    }
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "RESULT=ALL_PASSED" : $"RESULT=FAILURES:{failures}");
return failures == 0 ? 0 : 1;

// 枚举本进程的顶层窗口，找类名 #32770（标准对话框）且标题含"属性"
static IntPtr FindPropertiesDialog()
{
    IntPtr found = IntPtr.Zero;

    _ = NativeWindows.EnumWindows((hwnd, lparam) =>
    {
        uint _threadId = NativeWindows.GetWindowThreadProcessId(hwnd, out uint pid);

        if ((int)pid != Environment.ProcessId)
        {
            return true;
        }

        var className = new StringBuilder(256);
        _ = NativeWindows.GetClassName(hwnd, className, className.Capacity);

        if (className.ToString() != "#32770")
        {
            return true;
        }

        var title = new StringBuilder(256);
        _ = NativeWindows.GetWindowText(hwnd, title, title.Capacity);

        if (title.ToString().Contains("属性", StringComparison.Ordinal)
            || title.ToString().Contains("Properties", StringComparison.OrdinalIgnoreCase))
        {
            found = hwnd;
            return false;
        }

        return true;
    }, IntPtr.Zero);

    return found;
}
