using Files.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class NavigationTreeBuilderTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilesNav_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(entry, FileAttributes.Normal); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public void BuildRoot_WithDrives_ContainsCommonGroupAndDriveGroup()
    {
        IReadOnlyList<NavigationNode> nodes = new NavigationTreeBuilder().BuildRoot(includeDrives: true);

        Assert.IsTrue(nodes.Any(n => n.Kind == NavigationNodeKind.Group && n.DisplayName == "常用位置"));
        Assert.IsTrue(nodes.Any(n => n.Kind == NavigationNodeKind.Group && n.DisplayName == "此电脑"));
    }

    [TestMethod]
    public void BuildRoot_WithoutDrives_OmitsDriveGroup()
    {
        IReadOnlyList<NavigationNode> nodes = new NavigationTreeBuilder().BuildRoot(includeDrives: false);

        Assert.IsFalse(nodes.Any(n => n.DisplayName == "此电脑"));
    }

    [TestMethod]
    public void BuildRoot_GroupNodesAreNotNavigable()
    {
        IReadOnlyList<NavigationNode> nodes = new NavigationTreeBuilder().BuildRoot();

        foreach (NavigationNode group in nodes.Where(n => n.Kind == NavigationNodeKind.Group))
        {
            Assert.IsFalse(group.IsNavigable, "分组节点不应可导航。");
            Assert.IsNull(group.Path);
        }
    }

    [TestMethod]
    public void BuildRoot_KnownFoldersAreNavigableAndExist()
    {
        IReadOnlyList<NavigationNode> nodes = new NavigationTreeBuilder().BuildRoot(includeDrives: false);

        foreach (NavigationNode node in nodes.Where(n => n.Kind == NavigationNodeKind.KnownFolder))
        {
            Assert.IsTrue(node.IsNavigable);
            Assert.IsNotNull(node.Path);
            Assert.IsTrue(Directory.Exists(node.Path), $"已知文件夹应存在：{node.Path}");
        }
    }

    [TestMethod]
    public void BuildDrives_ReturnsOnlyReadyDrives()
    {
        IReadOnlyList<NavigationNode> drives = new NavigationTreeBuilder().BuildDrives();

        // 至少应有一个就绪的驱动器（运行环境一定有系统盘）
        Assert.IsNotEmpty(drives);

        foreach (NavigationNode drive in drives)
        {
            Assert.AreEqual(NavigationNodeKind.Drive, drive.Kind);
            Assert.IsTrue(drive.IsNavigable);
            Assert.IsTrue(Directory.Exists(drive.Path));
        }
    }

    [TestMethod]
    public void GetChildFolders_ReturnsSubdirectoriesOnly()
    {
        Directory.CreateDirectory(Path.Combine(_root, "alpha"));
        Directory.CreateDirectory(Path.Combine(_root, "beta"));
        File.WriteAllText(Path.Combine(_root, "file.txt"), "x");

        IReadOnlyList<NavigationNode> children = new NavigationTreeBuilder().GetChildFolders(_root);

        Assert.HasCount(2, children);
        Assert.IsTrue(children.All(c => c.Kind == NavigationNodeKind.Folder));
        Assert.IsFalse(children.Any(c => c.DisplayName == "file.txt"), "文件不应出现在导航树中。");
    }

    [TestMethod]
    public void GetChildFolders_SortsByName()
    {
        Directory.CreateDirectory(Path.Combine(_root, "zeta"));
        Directory.CreateDirectory(Path.Combine(_root, "Alpha"));

        IReadOnlyList<NavigationNode> children = new NavigationTreeBuilder().GetChildFolders(_root);

        Assert.AreEqual("Alpha", children[0].DisplayName);
        Assert.AreEqual("zeta", children[1].DisplayName);
    }

    [TestMethod]
    public void GetChildFolders_SkipsHiddenAndSystemDirectories()
    {
        string hidden = Path.Combine(_root, "hiddenDir");
        Directory.CreateDirectory(hidden);
        File.SetAttributes(hidden, FileAttributes.Hidden);

        Directory.CreateDirectory(Path.Combine(_root, "visibleDir"));

        IReadOnlyList<NavigationNode> children = new NavigationTreeBuilder().GetChildFolders(_root);

        Assert.HasCount(1, children);
        Assert.AreEqual("visibleDir", children[0].DisplayName);
    }

    [TestMethod]
    public void GetChildFolders_MissingDirectory_ReturnsEmpty()
    {
        IReadOnlyList<NavigationNode> children =
            new NavigationTreeBuilder().GetChildFolders(Path.Combine(_root, "nope"));

        Assert.IsEmpty(children);
    }

    [TestMethod]
    public void GetChildFolders_BlankPath_ReturnsEmpty()
    {
        Assert.IsEmpty(new NavigationTreeBuilder().GetChildFolders("  "));
    }

    [TestMethod]
    public void GetChildFolders_EmptyDirectory_ReturnsEmpty()
    {
        Assert.IsEmpty(new NavigationTreeBuilder().GetChildFolders(_root));
    }

    [TestMethod]
    public void GetChildFolders_ChildrenHaveFullPath()
    {
        Directory.CreateDirectory(Path.Combine(_root, "child"));

        NavigationNode child = new NavigationTreeBuilder().GetChildFolders(_root).Single();

        Assert.AreEqual(Path.Combine(_root, "child"), child.Path);
        Assert.IsTrue(child.HasChildren, "目录一律认为可能有子项，以便显示展开箭头。");
    }
}
