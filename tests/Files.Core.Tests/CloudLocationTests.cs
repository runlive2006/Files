using Files.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class ReparsePointPolicyTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilesReparse_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (string sub in Directory.EnumerateDirectories(_root))
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

    [TestMethod]
    public void NormalDirectory_IsTraversable()
    {
        string dir = Path.Combine(_root, "normal");
        Directory.CreateDirectory(dir);

        FileAttributes attributes = File.GetAttributes(dir);

        Assert.IsTrue(ReparsePointPolicy.IsTraversable(dir, attributes));
    }

    [TestMethod]
    public void RealJunction_IsNotTraversable()
    {
        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);

        string junction = Path.Combine(_root, "link");

        if (!TryCreateJunction(junction, target))
        {
            Assert.Inconclusive("当前环境无法创建 junction，跳过该用例。");
            return;
        }

        FileAttributes attributes = File.GetAttributes(junction);

        Assert.AreNotEqual(0, (int)(attributes & FileAttributes.ReparsePoint), "junction 应带重解析点属性。");
        Assert.IsFalse(
            ReparsePointPolicy.IsTraversable(junction, attributes),
            "真实链接必须不跟随，否则会循环。");
    }

    [TestMethod]
    public void NonReparseAttribute_IsAlwaysTraversable()
    {
        // 属性里没有 ReparsePoint 时无需读磁盘即可判定
        Assert.IsTrue(ReparsePointPolicy.IsTraversable(@"Z:\whatever", FileAttributes.Directory));
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

[TestClass]
public sealed class CloudLocationNavigationTests
{
    /// <summary>可注入的假云盘探测器。</summary>
    private sealed class FakeCloudProvider : ICloudLocationProvider
    {
        private readonly IReadOnlyList<CloudLocation> _locations;

        public FakeCloudProvider(params CloudLocation[] locations) => _locations = locations;

        public IReadOnlyList<CloudLocation> Detect() => _locations;
    }

    [TestMethod]
    public void BuildRoot_WithoutCloudProvider_HasNoCloudGroup()
    {
        IReadOnlyList<NavigationNode> nodes = new NavigationTreeBuilder(null).BuildRoot(includeDrives: false);

        Assert.IsFalse(nodes.Any(n => n.DisplayName == "云盘"));
    }

    [TestMethod]
    public void BuildRoot_WithCloudProvider_AddsCloudGroupAndNodes()
    {
        string path = Path.GetTempPath();
        var provider = new FakeCloudProvider(new CloudLocation("WPS云盘", path, "wps"));

        IReadOnlyList<NavigationNode> nodes = new NavigationTreeBuilder(provider).BuildRoot(includeDrives: false);

        NavigationNode? group = nodes.FirstOrDefault(n => n.DisplayName == "云盘");
        Assert.IsNotNull(group);
        Assert.AreEqual(NavigationNodeKind.Group, group.Kind);
        Assert.IsFalse(group.IsNavigable, "云盘分组本身不可导航。");

        NavigationNode? cloud = nodes.FirstOrDefault(n => n.Kind == NavigationNodeKind.Cloud);
        Assert.IsNotNull(cloud);
        Assert.AreEqual("WPS云盘", cloud.DisplayName);
        Assert.AreEqual(path, cloud.Path);
        Assert.IsTrue(cloud.IsNavigable);
    }

    [TestMethod]
    public void BuildRoot_CloudProviderReturnsEmpty_NoCloudGroup()
    {
        var provider = new FakeCloudProvider();

        IReadOnlyList<NavigationNode> nodes = new NavigationTreeBuilder(provider).BuildRoot(includeDrives: false);

        Assert.IsFalse(nodes.Any(n => n.DisplayName == "云盘"), "没有云盘时不应出现空的云盘分组。");
    }

    [TestMethod]
    public void BuildRoot_MultipleClouds_AllListed()
    {
        string path = Path.GetTempPath();
        var provider = new FakeCloudProvider(
            new CloudLocation("WPS云盘", path, "wps"),
            new CloudLocation("某网盘", path, "other"));

        IReadOnlyList<NavigationNode> nodes = new NavigationTreeBuilder(provider).BuildRoot(includeDrives: false);

        Assert.HasCount(2, nodes.Where(n => n.Kind == NavigationNodeKind.Cloud));
    }

    [TestMethod]
    public void CloudNode_CanBeExpanded_ToListSubdirectories()
    {
        // 用临时目录模拟云盘根：验证云盘节点与普通目录走同一套枚举
        string root = Path.Combine(Path.GetTempPath(), "CloudNav_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "01 效率"));
        Directory.CreateDirectory(Path.Combine(root, "02 学习"));

        try
        {
            var provider = new FakeCloudProvider(new CloudLocation("测试云盘", root, "test"));
            IReadOnlyList<NavigationNode> nodes = new NavigationTreeBuilder(provider).BuildRoot(includeDrives: false);
            NavigationNode cloud = nodes.First(n => n.Kind == NavigationNodeKind.Cloud);

            IReadOnlyList<NavigationNode> children =
                new NavigationTreeBuilder(provider).GetChildFolders(cloud.Path!);

            Assert.HasCount(2, children);
            Assert.IsTrue(children.All(c => c.Kind == NavigationNodeKind.Folder));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
