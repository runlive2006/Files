using Files.Core.Models;
using Files.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class LayoutFactoryTests
{
    private static TabState Tab(string path) => new() { Path = path, ViewMode = ViewMode.Details };

    private static LayoutNode SinglePane(string path = @"C:\Work") => new PaneNode
    {
        Pane = new PaneState { Tabs = [Tab(path)], ActiveTabIndex = 0 },
    };

    [TestMethod]
    public void Create_Single_ProducesOnePane()
    {
        var root = LayoutFactory.Create(SinglePane(), PaneLayout.Single);
        Assert.AreEqual(1, LayoutFactory.CountPanes(root));
    }

    [TestMethod]
    public void Create_HorizontalSplit_ProducesTwoPanes()
    {
        var root = LayoutFactory.Create(SinglePane(), PaneLayout.HorizontalSplit);
        Assert.AreEqual(2, LayoutFactory.CountPanes(root));
        Assert.IsInstanceOfType<SplitNode>(root);
        Assert.AreEqual(SplitDirection.Horizontal, ((SplitNode)root).Direction);
    }

    [TestMethod]
    public void Create_VerticalSplit_ProducesTwoPanes()
    {
        var root = LayoutFactory.Create(SinglePane(), PaneLayout.VerticalSplit);
        Assert.AreEqual(2, LayoutFactory.CountPanes(root));
        Assert.AreEqual(SplitDirection.Vertical, ((SplitNode)root).Direction);
    }

    [TestMethod]
    public void Create_Quad_ProducesFourPanes_InTwoByTwo()
    {
        var root = LayoutFactory.Create(SinglePane(), PaneLayout.Quad);

        Assert.AreEqual(4, LayoutFactory.CountPanes(root));

        // 顶层左右分裂，两个子节点各自上下分裂（PRD §2.1 的生成规则）
        var outer = (SplitNode)root;
        Assert.AreEqual(SplitDirection.Horizontal, outer.Direction);
        Assert.AreEqual(SplitDirection.Vertical, ((SplitNode)outer.First).Direction);
        Assert.AreEqual(SplitDirection.Vertical, ((SplitNode)outer.Second).Direction);
    }

    [TestMethod]
    public void Create_QuadFromVerticalSplit_StillProducesFourPanes()
    {
        var vertical = LayoutFactory.Create(SinglePane(), PaneLayout.VerticalSplit);
        var quad = LayoutFactory.Create(vertical, PaneLayout.Quad);

        Assert.AreEqual(4, LayoutFactory.CountPanes(quad));
    }

    [TestMethod]
    public void Create_NewPanesInheritSourcePath_ButAreIndependentInstances()
    {
        var root = LayoutFactory.Create(SinglePane(@"D:\Data"), PaneLayout.HorizontalSplit);
        var panes = LayoutFactory.CollectPanes(root);

        Assert.AreEqual(@"D:\Data", panes[0].ActiveTab.Path);
        Assert.AreEqual(@"D:\Data", panes[1].ActiveTab.Path);

        // 独立实例：改一个不影响另一个
        panes[1].ActiveTab.Path = @"E:\Other";
        Assert.AreEqual(@"D:\Data", panes[0].ActiveTab.Path);
        Assert.AreNotSame(panes[0].ActiveTab, panes[1].ActiveTab);
    }

    [TestMethod]
    public void CollectPanes_PreservesLeftToRightOrder()
    {
        var left = SinglePane(@"C:\Left");
        var right = SinglePane(@"C:\Right");
        var root = new SplitNode
        {
            Direction = SplitDirection.Horizontal,
            First = left,
            Second = right,
        };

        var panes = LayoutFactory.CollectPanes(root);

        Assert.AreEqual(@"C:\Left", panes[0].ActiveTab.Path);
        Assert.AreEqual(@"C:\Right", panes[1].ActiveTab.Path);
    }

    [TestMethod]
    public void Create_NullCurrent_StartsFromDefaultPane()
    {
        var root = LayoutFactory.Create(null, PaneLayout.Single);
        Assert.AreEqual(1, LayoutFactory.CountPanes(root));
    }

    [TestMethod]
    public void ActiveTab_WithMultipleTabs_ReturnsSelectedOne()
    {
        var pane = new PaneState
        {
            Tabs = [Tab(@"C:\A"), Tab(@"C:\B")],
            ActiveTabIndex = 1,
        };

        Assert.AreEqual(@"C:\B", pane.ActiveTab.Path);
    }
}
