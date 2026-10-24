using Files.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class BreadcrumbBuilderTests
{
    [TestMethod]
    public void Build_DrivePath_SplitsIntoRootAndSegments()
    {
        IReadOnlyList<BreadcrumbSegment> segments = BreadcrumbBuilder.Build(@"C:\Users\alice\Docs");

        Assert.HasCount(4, segments);
        Assert.AreEqual("C:", segments[0].DisplayName);
        Assert.AreEqual(@"C:\", segments[0].FullPath);
        Assert.AreEqual("Users", segments[1].DisplayName);
        Assert.AreEqual(@"C:\Users", segments[1].FullPath);
        Assert.AreEqual("alice", segments[2].DisplayName);
        Assert.AreEqual(@"C:\Users\alice", segments[2].FullPath);
        Assert.AreEqual("Docs", segments[3].DisplayName);
        Assert.AreEqual(@"C:\Users\alice\Docs", segments[3].FullPath);
    }

    [TestMethod]
    public void Build_DriveRoot_ReturnsSingleSegment()
    {
        IReadOnlyList<BreadcrumbSegment> segments = BreadcrumbBuilder.Build(@"C:\");

        Assert.HasCount(1, segments);
        Assert.AreEqual("C:", segments[0].DisplayName);
        Assert.AreEqual(@"C:\", segments[0].FullPath);
    }

    [TestMethod]
    public void Build_DriveRootWithoutSeparator_IsNormalized()
    {
        IReadOnlyList<BreadcrumbSegment> segments = BreadcrumbBuilder.Build("C:");

        Assert.HasCount(1, segments);
        Assert.AreEqual(@"C:\", segments[0].FullPath);
    }

    [TestMethod]
    public void Build_WithRootDisplayName_UsesIt()
    {
        IReadOnlyList<BreadcrumbSegment> segments =
            BreadcrumbBuilder.Build(@"C:\Users", rootDisplayName: "Win11 (C:)");

        Assert.AreEqual("Win11 (C:)", segments[0].DisplayName);
    }

    [TestMethod]
    public void Build_UncPath_KeepsServerShareAsOneSegment()
    {
        IReadOnlyList<BreadcrumbSegment> segments =
            BreadcrumbBuilder.Build(@"\\server\share\folder\sub");

        Assert.HasCount(3, segments);
        Assert.AreEqual(@"\\server\share", segments[0].DisplayName);
        Assert.AreEqual(@"\\server\share", segments[0].FullPath);
        Assert.AreEqual("folder", segments[1].DisplayName);
        Assert.AreEqual(@"\\server\share\folder", segments[1].FullPath);
        Assert.AreEqual("sub", segments[2].DisplayName);
    }

    [TestMethod]
    public void Build_UncShareRoot_ReturnsSingleSegment()
    {
        IReadOnlyList<BreadcrumbSegment> segments = BreadcrumbBuilder.Build(@"\\server\share");

        Assert.HasCount(1, segments);
        Assert.AreEqual(@"\\server\share", segments[0].FullPath);
    }

    [TestMethod]
    public void Build_TrailingSeparator_IsIgnored()
    {
        IReadOnlyList<BreadcrumbSegment> segments = BreadcrumbBuilder.Build(@"C:\Users\alice\");

        Assert.HasCount(3, segments);
        Assert.AreEqual("alice", segments[2].DisplayName);
        Assert.AreEqual(@"C:\Users\alice", segments[2].FullPath);
    }

    [TestMethod]
    public void Build_EachSegmentPathIsClickablePrefix()
    {
        // 每个分段的 FullPath 必须是可以直接导航的路径前缀
        IReadOnlyList<BreadcrumbSegment> segments = BreadcrumbBuilder.Build(@"D:\Work\Project\src");

        Assert.IsTrue(segments.All(s => Path.IsPathFullyQualified(s.FullPath)));
        Assert.AreEqual(@"D:\Work\Project\src", segments[^1].FullPath);
    }

    [TestMethod]
    public void Build_ChineseSegments_ArePreserved()
    {
        IReadOnlyList<BreadcrumbSegment> segments = BreadcrumbBuilder.Build(@"D:\文档\项目 周报");

        Assert.AreEqual("文档", segments[1].DisplayName);
        Assert.AreEqual("项目 周报", segments[2].DisplayName);
    }

    [TestMethod]
    public void Build_NullOrBlank_ReturnsEmpty()
    {
        Assert.IsEmpty(BreadcrumbBuilder.Build(null));
        Assert.IsEmpty(BreadcrumbBuilder.Build("   "));
    }

    [TestMethod]
    public void GetRootDisplayName_ForTempDrive_ReturnsNonEmpty()
    {
        string name = BreadcrumbBuilder.GetRootDisplayName(Path.GetTempPath());

        Assert.IsFalse(string.IsNullOrWhiteSpace(name));
        StringAssert.Contains(name, ":");
    }

    [TestMethod]
    public void GetRootDisplayName_InvalidPath_FallsBack()
    {
        string name = BreadcrumbBuilder.GetRootDisplayName("not-a-real-path-xyz");

        Assert.IsFalse(string.IsNullOrWhiteSpace(name));
    }
}
