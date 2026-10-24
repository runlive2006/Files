using Files.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class DetailsPaneFormatterTests
{
    private static readonly DateTimeOffset Modified = new(2026, 10, 20, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Created = new(2026, 1, 15, 3, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void NoSelection_WithoutDirectory_ReturnsEmpty()
    {
        DetailsPaneContent content = DetailsPaneFormatter.NoSelection(null, null);

        Assert.AreEqual("未选择项目", content.PrimaryText);
        Assert.AreEqual(string.Empty, content.DetailText);
    }

    [TestMethod]
    public void NoSelection_WithSummary_ShowsDirectoryCounts()
    {
        var summary = new DirectorySummary(FolderCount: 3, FileCount: 12);

        DetailsPaneContent content = DetailsPaneFormatter.NoSelection(@"C:\Work", summary);

        Assert.AreEqual("Work", content.PrimaryText);
        Assert.AreEqual(@"C:\Work", content.SecondaryText);
        Assert.AreEqual("3 个文件夹，12 个文件", content.DetailText);
        Assert.IsFalse(content.IsPending);
    }

    [TestMethod]
    public void NoSelection_WithoutSummary_ShowsPathOnly()
    {
        DetailsPaneContent content = DetailsPaneFormatter.NoSelection(@"C:\Work", null);

        Assert.AreEqual("Work", content.PrimaryText);
        Assert.AreEqual(@"C:\Work", content.SecondaryText);
        Assert.AreEqual(string.Empty, content.DetailText);
    }

    [TestMethod]
    public void SingleFile_ShowsNameTypeSizeAndTimes()
    {
        DetailsPaneContent content = DetailsPaneFormatter.SingleFile(
            @"C:\Work\report.PDF", 2048, Modified, Created);

        Assert.AreEqual("report.PDF", content.PrimaryText);
        StringAssert.Contains(content.SecondaryText, "PDF 文件");
        StringAssert.Contains(content.DetailText, "2.0 KB");
        StringAssert.Contains(content.DetailText, "修改：");
        StringAssert.Contains(content.DetailText, "创建：");
    }

    [TestMethod]
    public void SingleFile_WithoutExtension_IsPlainFile()
    {
        DetailsPaneContent content = DetailsPaneFormatter.SingleFile(
            @"C:\Work\LICENSE", 100, Modified, Created);

        StringAssert.Contains(content.SecondaryText, "文件");
        Assert.DoesNotContain("LICENSE 文件", content.SecondaryText);
    }

    [TestMethod]
    public void SingleFolder_WithSummary_ShowsCounts()
    {
        var summary = new DirectorySummary(5, 0);

        DetailsPaneContent content = DetailsPaneFormatter.SingleFolder(@"C:\Work\Docs", summary, Modified);

        Assert.AreEqual("Docs", content.PrimaryText);
        StringAssert.Contains(content.SecondaryText, "文件夹");
        StringAssert.Contains(content.DetailText, "5 个文件夹");
        Assert.IsFalse(content.IsPending);
    }

    [TestMethod]
    public void SingleFolder_WithoutSummary_IsPending()
    {
        DetailsPaneContent content = DetailsPaneFormatter.SingleFolder(@"C:\Work\Docs", null, Modified);

        Assert.IsTrue(content.IsPending, "统计未完成时应标记为 pending。");
        StringAssert.Contains(content.DetailText, "正在统计");
    }

    [TestMethod]
    public void SingleFolder_DriveRoot_UsesFullPathAsName()
    {
        DetailsPaneContent content = DetailsPaneFormatter.SingleFolder(@"C:\", new DirectorySummary(1, 2), Modified);

        Assert.AreEqual(@"C:\", content.PrimaryText);
    }

    [TestMethod]
    public void Multiple_AllFiles_ShowsTotalSize()
    {
        DetailsPaneContent content = DetailsPaneFormatter.Multiple(count: 3, totalSizeBytes: 3072, folderCount: 0);

        Assert.AreEqual("已选择 3 项", content.PrimaryText);
        StringAssert.Contains(content.DetailText, "全部为文件");
        StringAssert.Contains(content.DetailText, "3.0 KB");
    }

    [TestMethod]
    public void Multiple_AllFolders_SaysNoSize()
    {
        DetailsPaneContent content = DetailsPaneFormatter.Multiple(count: 2, totalSizeBytes: 0, folderCount: 2);

        StringAssert.Contains(content.DetailText, "全部为文件夹");
        Assert.DoesNotContain("合计", content.DetailText);
    }

    [TestMethod]
    public void Multiple_Mixed_ShowsComposition()
    {
        DetailsPaneContent content = DetailsPaneFormatter.Multiple(count: 5, totalSizeBytes: 100, folderCount: 2);

        StringAssert.Contains(content.DetailText, "2 个文件夹");
        StringAssert.Contains(content.DetailText, "3 个文件");
    }

    [TestMethod]
    public void Multiple_Zero_ReturnsEmpty()
    {
        DetailsPaneContent content = DetailsPaneFormatter.Multiple(0, null, 0);

        Assert.AreEqual("未选择项目", content.PrimaryText);
    }

    [TestMethod]
    public void FormatSize_UsesReadableUnits()
    {
        Assert.AreEqual("512 B", DetailsPaneFormatter.FormatSize(512));
        Assert.AreEqual("1.0 KB", DetailsPaneFormatter.FormatSize(1024));
        Assert.AreEqual("1.0 MB", DetailsPaneFormatter.FormatSize(1024 * 1024));
        Assert.AreEqual("1.00 GB", DetailsPaneFormatter.FormatSize(1024L * 1024 * 1024));
    }

    [TestMethod]
    public void SingleFile_RejectsBlankPath()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => DetailsPaneFormatter.SingleFile("  ", 1, Modified, Created));
    }
}

[TestClass]
public sealed class DirectorySummaryServiceTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilesSummary_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public void Summarize_CountsDirectChildrenOnly()
    {
        Directory.CreateDirectory(Path.Combine(_root, "dir1"));
        Directory.CreateDirectory(Path.Combine(_root, "dir2"));

        // 子目录里的文件不应计入
        File.WriteAllText(Path.Combine(_root, "dir1", "nested.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "a.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "c.txt"), "x");

        DirectorySummary summary = new DirectorySummaryService().Summarize(_root)!.Value;

        Assert.AreEqual(2, summary.FolderCount);
        Assert.AreEqual(3, summary.FileCount);
        Assert.AreEqual(5, summary.TotalCount);
    }

    [TestMethod]
    public void Summarize_EmptyDirectory_ReturnsZeros()
    {
        DirectorySummary summary = new DirectorySummaryService().Summarize(_root)!.Value;

        Assert.IsTrue(summary.IsEmpty);
        Assert.AreEqual("空文件夹", summary.Describe());
    }

    [TestMethod]
    public void Summarize_MissingDirectory_ReturnsNull()
    {
        Assert.IsNull(new DirectorySummaryService().Summarize(Path.Combine(_root, "nope")));
    }

    [TestMethod]
    public void Summarize_BlankPath_ReturnsNull()
    {
        Assert.IsNull(new DirectorySummaryService().Summarize("  "));
    }

    [TestMethod]
    public void Describe_LabelsSingularCases()
    {
        Assert.AreEqual("3 个文件", new DirectorySummary(0, 3).Describe());
        Assert.AreEqual("2 个文件夹", new DirectorySummary(2, 0).Describe());
        Assert.AreEqual("1 个文件夹，4 个文件", new DirectorySummary(1, 4).Describe());
    }
}
