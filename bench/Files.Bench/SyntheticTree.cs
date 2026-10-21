using System.Globalization;
using System.Text;

namespace Files.Bench;

/// <summary>
/// 生成用于基准测试的目录树（默认在临时目录下）。
/// </summary>
internal static class SyntheticTree
{
    private static readonly string[] Extensions = [".txt", ".log", ".cs", ".png", ".json", ".bin"];

    /// <summary>
    /// 确保目标目录中有 <paramref name="count"/> 个文件，并返回目录路径。
    /// </summary>
    /// <param name="root">根目录。</param>
    /// <param name="count">文件数量。</param>
    /// <param name="reuse">若已存在且数量一致则跳过生成。</param>
    public static string Ensure(string root, int count, bool reuse)
    {
        Directory.CreateDirectory(root);

        int existing = Directory.EnumerateFiles(root).Take(count + 1).Count();
        if (reuse && existing >= count)
        {
            Console.WriteLine($"复用已有数据：{root}（{existing:N0} 个文件）");
            return root;
        }

        Console.WriteLine($"正在生成 {count:N0} 个文件到 {root} ...");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // 分批推进并复用同一批文件名模式，避免生成过程本身成为瓶颈
        for (int i = 0; i < count; i++)
        {
            string extension = Extensions[i % Extensions.Length];
            string name = string.Create(
                CultureInfo.InvariantCulture,
                $"sample-{i:D7}{extension}");
            string path = Path.Combine(root, name);

            if (File.Exists(path))
            {
                continue;
            }

            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1);
            stream.WriteByte((byte)(i & 0xFF));
        }

        stopwatch.Stop();
        Console.WriteLine($"生成完成，用时 {stopwatch.Elapsed.TotalSeconds:F1}s");
        return root;
    }

    /// <summary>删除基准目录。</summary>
    public static void Cleanup(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"警告：清理 {root} 失败（{ex.Message}），可手动删除。");
        }
    }

    /// <summary>把字节数格式化为易读字符串。</summary>
    public static string Bytes(long value) => value switch
    {
        < 1024 => $"{value} B",
        < 1024 * 1024 => $"{value / 1024.0:F1} KB",
        _ => $"{value / (1024.0 * 1024.0):F2} MB",
    };

    /// <summary>构造 UTF-8 输出，保证中文与表格对齐。</summary>
    public static void ConfigureConsole()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // 某些重定向环境下不允许改编码，忽略即可
        }
    }
}
