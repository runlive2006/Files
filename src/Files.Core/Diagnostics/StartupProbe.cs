using System.Diagnostics;

namespace Files.Core.Diagnostics;

/// <summary>
/// 启动阶段打点。用于验证 PRD §5.1 的"冷启动 ≤400ms / 热启动 ≤150ms"。
/// </summary>
/// <remarks>
/// 时间基准取进程创建时刻（<see cref="Process.StartTime"/>），因此包含运行时初始化、
/// JIT、程序集加载等全部成本 —— 这才是用户真实感知的启动时间。
/// </remarks>
public static class StartupProbe
{
    private static readonly List<MarkEntry> Marks = [];
    private static readonly object Gate = new();
    private static readonly DateTimeOffset ProcessStart = ResolveProcessStart();

    /// <summary>是否启用打点（Release 下也可常开，开销仅为几次时钟读取）。</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>自进程创建以来的毫秒数。</summary>
    public static double SinceProcessStartMs =>
        (DateTimeOffset.UtcNow - ProcessStart).TotalMilliseconds;

    /// <summary>记录一个打点。</summary>
    /// <param name="name">打点名称，例如 <c>app-startup</c>、<c>first-render</c>。</param>
    public static void Mark(string name)
    {
        if (!Enabled)
        {
            return;
        }

        var mark = new MarkEntry(name, SinceProcessStartMs);

        lock (Gate)
        {
            Marks.Add(mark);
        }

        Debug.WriteLine($"[startup] {mark.Name} = {mark.ElapsedMs:F1} ms");
    }

    /// <summary>返回全部打点的单行摘要，便于写日志或显示在状态栏。</summary>
    public static string Report()
    {
        lock (Gate)
        {
            if (Marks.Count == 0)
            {
                return "（无启动打点）";
            }

            return string.Join(" | ", Marks.Select(m => $"{m.Name}={m.ElapsedMs:F1}ms"));
        }
    }

    /// <summary>取某个打点的耗时；不存在时返回 <c>null</c>。</summary>
    public static double? GetMark(string name)
    {
        lock (Gate)
        {
            foreach (var mark in Marks)
            {
                if (mark.Name == name)
                {
                    return mark.ElapsedMs;
                }
            }
        }

        return null;
    }

    /// <summary>清空打点（测试用）。</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Marks.Clear();
        }
    }

    private static DateTimeOffset ResolveProcessStart()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return new DateTimeOffset(process.StartTime.ToUniversalTime());
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // 某些受限环境下拿不到进程启动时间，退化为"首次访问时刻"，此时数值偏小
            return DateTimeOffset.UtcNow;
        }
    }

    private readonly record struct MarkEntry(string Name, double ElapsedMs);
}
