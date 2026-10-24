using Files.Core.Models;
using Files.Core.Services;
using Files.Core.Services.Search;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class RecursiveSearcherTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilesSearch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        // junction 必须先单独删除（递归删除会因重解析点失败）
        foreach (string sub in Directory.EnumerateDirectories(_root, "*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(sub, recursive: false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private SearchResult Search(string query, bool includeHidden = false) =>
        new RecursiveSearcher().Search(_root, SearchQueryParser.Parse(query), includeHidden);

    [TestMethod]
    public void Search_FindsFilesInSubdirectories()
    {
        Directory.CreateDirectory(Path.Combine(_root, "a", "b"));
        File.WriteAllText(Path.Combine(_root, "top.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "a", "mid.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "a", "b", "deep.txt"), "x");

        SearchResult result = Search("ext:.txt");

        Assert.HasCount(3, result.Hits);
    }

    [TestMethod]
    public void Search_ExcludesNonMatching()
    {
        File.WriteAllText(Path.Combine(_root, "keep.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "skip.log"), "x");

        SearchResult result = Search("ext:.txt");

        Assert.HasCount(1, result.Hits);
        Assert.AreEqual("keep.txt", result.Hits[0].Entry.Name);
    }

    [TestMethod]
    public void Search_EmptyQuery_MatchesEverything()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "x");
        Directory.CreateDirectory(Path.Combine(_root, "sub"));

        SearchResult result = Search("");

        // a.txt + sub + 根下无其他项
        Assert.IsGreaterThanOrEqualTo(2, result.Count, "空查询应匹配所有条目。");
    }

    [TestMethod]
    public void Search_SkipsHiddenWhenDisabled()
    {
        string hidden = Path.Combine(_root, "h.txt");
        File.WriteAllText(hidden, "x");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        Assert.IsFalse(Search("").Hits.Any(h => h.Entry.Name == "h.txt"));
        Assert.IsTrue(Search("", includeHidden: true).Hits.Any(h => h.Entry.Name == "h.txt"));
    }

    [TestMethod]
    public void Search_DoesNotFollowReparsePoints()
    {
        // 用 junction 模拟符号链接：指向父目录会形成循环
        string loop = Path.Combine(_root, "loop");
        string target = _root;

        if (!TryCreateJunction(loop, target))
        {
            Assert.Inconclusive("当前环境无法创建 junction，跳过该用例。");
            return;
        }

        File.WriteAllText(Path.Combine(_root, "f.txt"), "x");

        // 若跟随 junction 会无限递归，这里必须能正常返回
        SearchResult result = Search("ext:.txt");

        Assert.IsLessThan(100, result.Count, "不应因跟随重解析点而爆炸式增长。");
    }

    [TestMethod]
    public void Search_ReportsScannedCount()
    {
        for (int i = 0; i < 10; i++)
        {
            File.WriteAllText(Path.Combine(_root, $"f{i}.txt"), "x");
        }

        var progresses = new List<SearchProgress>();
        var progress = new Progress<SearchProgress>(p => progresses.Add(p));

        new RecursiveSearcher().Search(_root, SearchQueryParser.Parse(""), false, progress);
        Thread.Sleep(200);

        Assert.IsNotEmpty(progresses);
    }

    [TestMethod]
    public void Search_Cancellation_Throws()
    {
        for (int i = 0; i < 50; i++)
        {
            Directory.CreateDirectory(Path.Combine(_root, $"d{i}"));
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(
            () => new RecursiveSearcher().Search(
                _root, SearchQueryParser.Parse(""), false, null, cts.Token));
    }

    [TestMethod]
    public void Search_MissingRoot_ReturnsEmptyWithoutThrowing()
    {
        SearchResult result = new RecursiveSearcher().Search(
            Path.Combine(_root, "nope"), SearchQueryParser.Parse(""), false);

        Assert.IsEmpty(result.Hits);
        Assert.AreEqual(1, result.SkippedDirectoryCount, "不存在的根目录应计入跳过数。");
    }

    [TestMethod]
    public void Search_RejectsBlankRoot()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new RecursiveSearcher().Search("  ", SearchQueryParser.Parse(""), false));
    }

    [TestMethod]
    public void Search_CombinesConditions()
    {
        File.WriteAllText(Path.Combine(_root, "big.txt"), new string('x', 2000));
        File.WriteAllText(Path.Combine(_root, "small.txt"), "x");

        SearchResult result = Search("ext:.txt size:>1kb");

        Assert.HasCount(1, result.Hits);
        Assert.AreEqual("big.txt", result.Hits[0].Entry.Name);
    }

    private static bool TryCreateJunction(string junctionPath, string targetPath)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit(5000);
            return process.ExitCode == 0 && Directory.Exists(junctionPath);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}