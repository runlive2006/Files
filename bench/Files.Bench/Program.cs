using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Files.Core.Diagnostics;
using Files.Core.Models;
using Files.Core.Services;
using Files.Interop;

namespace Files.Bench;

/// <summary>
/// 目录枚举性能基准台。对应 DEV-PLAN M1 任务 1.5。
/// </summary>
/// <remarks>
/// 这是本项目把"最高 UI 性能"从口号变成数字的资产：它测量的是进入目录时
/// 真实会跑的那条路径（系统调用 + 映射 + 排序），而不是合成的循环。
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        SyntheticTree.ConfigureConsole();

        var options = Options.Parse(args);
        if (options is null)
        {
            Options.PrintUsage();
            return 2;
        }

        PrintEnvironment();

        string directory = options.Directory
            ?? Path.Combine(Path.GetTempPath(), "FilesBench", $"n{options.Count}");

        try
        {
            SyntheticTree.Ensure(directory, options.Count, options.Reuse);

            // 预热：让 JIT 与文件系统缓存进入稳态，避免第一次测量被冷启动污染
            _ = Win32DirectoryEnumerator.ReadAll(directory);
            _ = new Win32DirectoryLister().List(directory);

            Console.WriteLine();
            Console.WriteLine($"=== 测量目标：{directory} ===");
            Console.WriteLine();

            var results = new List<Result>
            {
                MeasureBaselineEnumerate(directory, options.Rounds),
                MeasureInteropReadAll(directory, options.Rounds),
                MeasureLister(directory, options.Rounds),
                MeasureFirstScreen(directory, options.Rounds),
            };

            PrintResults(results, options.Count);

            bool failed = options.Gate && results.Any(r => r.BudgetMs > 0 && r.BestMs > r.BudgetMs);
            if (options.Gate)
            {
                Console.WriteLine();
                Console.WriteLine(failed ? "结论：未达标（超出预算）" : "结论：达标");
            }

            if (!options.Keep)
            {
                SyntheticTree.Cleanup(directory);
            }

            return failed ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"基准测试失败：{ex.Message}");
            return 3;
        }
    }

    private static Result MeasureBaselineEnumerate(string directory, int rounds)
    {
        return Run(
            "基线 Directory.EnumerateFileSystemEntries",
            directory,
            budgetPerItemMs: 0,
            () => Directory.EnumerateFileSystemEntries(directory).Count(),
            rounds);
    }

    private static Result MeasureInteropReadAll(string directory, int rounds)
    {
        return Run(
            "Interop Win32DirectoryEnumerator.ReadAll",
            directory,
            // 预算：10 万项 ≤600ms（PRD §5.1 进入目录首屏）折算为 0.006ms/项
            budgetPerItemMs: 0.006,
            () => Win32DirectoryEnumerator.ReadAll(directory).Length,
            rounds);
    }

    private static Result MeasureLister(string directory, int rounds)
    {
        var lister = new Win32DirectoryLister();
        return Run(
            "Win32DirectoryLister.List（枚举+映射+排序）",
            directory,
            // 排序与对象构造是真实成本，给 2 倍余量
            budgetPerItemMs: 0.012,
            () => lister.List(directory).Count,
            rounds);
    }

    private static Result MeasureFirstScreen(string directory, int rounds)
    {
        // 模拟首屏：枚举 + 只为可见的前 100 项构造显示字符串
        return Run(
            "首屏模拟（枚举 + 前 100 项格式化）",
            directory,
            budgetPerItemMs: 0.012,
            () =>
            {
                var entries = Win32DirectoryEnumerator.ReadAll(directory);
                int visible = Math.Min(100, entries.Length);
                int chars = 0;

                for (int i = 0; i < visible; i++)
                {
                    var entry = entries[i];
                    chars += entry.Name.Length;
                    chars += entry.IsDirectory ? 0 : entry.Size.ToString(CultureInfo.InvariantCulture).Length;
                    chars += entry.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture).Length;
                }

                // 防止编译器把上面的计算优化掉
                GC.KeepAlive(chars);
                return entries.Length;
            },
            rounds);
    }

    private static Result Run(string name, string directory, double budgetPerItemMs, Func<int> action, int rounds)
    {
        var times = new List<double>(rounds);
        long allocated = 0;
        int count = 0;

        var sampler = new WorkingSetSampler();
        double memoryDelta = sampler.MeasureDeltaMb(() =>
        {
            for (int i = 0; i < rounds; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                long before = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();

                count = action();

                double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                long after = GC.GetAllocatedBytesForCurrentThread();

                times.Add(elapsed);
                allocated = Math.Max(allocated, after - before);
            }
        });

        times.Sort();

        return new Result(
            name,
            BestMs: times[0],
            MedianMs: times[times.Count / 2],
            AllocatedBytes: allocated,
            ItemCount: count,
            MemoryDeltaMb: memoryDelta,
            BudgetMs: budgetPerItemMs > 0 ? budgetPerItemMs * count : 0);
    }

    private static void PrintResults(List<Result> results, int expectedCount)
    {
        Console.WriteLine(
            $"{"测量项",-44}{"项数",10}{"最快",10}{"中位",10}{"分配",12}{"内存增量",11}{"预算",9}   判定");
        Console.WriteLine(new string('-', 120));

        foreach (var result in results)
        {
            string verdict = result.BudgetMs <= 0
                ? "—"
                : (result.BestMs <= result.BudgetMs ? "达标" : "超预算");

            string budget = result.BudgetMs > 0
                ? result.BudgetMs.ToString("F0", CultureInfo.InvariantCulture) + "ms"
                : "—";

            Console.WriteLine(
                $"{result.Name,-44}{result.ItemCount,10:N0}{result.BestMs,9:F1}ms{result.MedianMs,9:F1}ms" +
                $"{SyntheticTree.Bytes(result.AllocatedBytes),12}{result.MemoryDeltaMb,10:F1}MB{budget,9}   {verdict}");
        }

        Console.WriteLine(new string('-', 120));

        var throughput = results.FirstOrDefault(r => r.Name.StartsWith("Interop", StringComparison.Ordinal));
        if (throughput is { BestMs: > 0 })
        {
            Console.WriteLine(
                $"Interop 吞吐：{throughput.ItemCount / throughput.BestMs * 1000:N0} 项/秒" +
                $"（期望项数 {expectedCount:N0}）");
        }
    }

    private static void PrintEnvironment()
    {
        Console.WriteLine("=== 环境 ===");
        Console.WriteLine($"OS            : {RuntimeInformation.OSDescription}");
        Console.WriteLine($"运行时        : {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"进程架构      : {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"CPU 逻辑核数  : {Environment.ProcessorCount}");
        Console.WriteLine($"GC 模式       : {(System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation")}");
        Console.WriteLine($"Build         : {(IsDebugBuild() ? "Debug（数值偏慢，建议 -c Release）" : "Release")}");
    }

    private static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    private sealed record Result(
        string Name,
        double BestMs,
        double MedianMs,
        long AllocatedBytes,
        int ItemCount,
        double MemoryDeltaMb,
        double BudgetMs);

    private sealed class Options
    {
        public int Count { get; private init; } = 50_000;

        public string? Directory { get; private init; }

        public bool Reuse { get; private init; }

        public bool Keep { get; private init; }

        public bool Gate { get; private init; }

        public int Rounds { get; private init; } = 3;

        public static Options? Parse(string[] args)
        {
            int count = 50_000;
            string? directory = null;
            bool reuse = false;
            bool keep = false;
            bool gate = false;
            int rounds = 3;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--count" when i + 1 < args.Length:
                        if (!int.TryParse(args[++i], CultureInfo.InvariantCulture, out count) || count <= 0)
                        {
                            Console.Error.WriteLine("--count 需要正整数。");
                            return null;
                        }

                        break;

                    case "--dir" when i + 1 < args.Length:
                        directory = args[++i];
                        break;

                    case "--rounds" when i + 1 < args.Length:
                        if (!int.TryParse(args[++i], CultureInfo.InvariantCulture, out rounds) || rounds <= 0)
                        {
                            Console.Error.WriteLine("--rounds 需要正整数。");
                            return null;
                        }

                        break;

                    case "--reuse":
                        reuse = true;
                        break;

                    case "--keep":
                        keep = true;
                        break;

                    case "--gate":
                        gate = true;
                        break;

                    case "--help" or "-h":
                        return null;

                    default:
                        Console.Error.WriteLine($"未知参数：{args[i]}");
                        return null;
                }
            }

            return new Options
            {
                Count = count,
                Directory = directory,
                Reuse = reuse,
                Keep = keep,
                Gate = gate,
                Rounds = rounds,
            };
        }

        public static void PrintUsage()
        {
            Console.WriteLine(
                """
                Files 目录枚举基准台

                用法：Files.Bench [选项]

                  --count <N>     生成/测量的文件数量（默认 50000）
                  --dir <PATH>    使用指定目录（默认在系统临时目录下）
                  --reuse         目录已存在时复用，不重新生成
                  --keep          测量后保留目录
                  --rounds <N>    每个测量项的重复轮数（默认 3，取最快值）
                  --gate          按预算判定，超预算时返回退出码 1（供 CI 使用）
                  -h, --help      显示帮助

                示例：
                  dotnet run -c Release --project bench/Files.Bench -- --count 100000 --gate
                """);
        }
    }
}
