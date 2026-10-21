using Files.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class PaneStateTabTests
{
    private static PaneState MakePane(params string[] paths)
    {
        var tabs = paths.Select(p => new TabState { Path = p }).ToList();
        return new PaneState { Tabs = tabs, ActiveTabIndex = 0 };
    }

    [TestMethod]
    public void AddTab_InsertsAfterActive_AndActivatesIt()
    {
        PaneState pane = MakePane(@"C:\A", @"C:\B");
        pane.Activate(0);

        TabState added = pane.AddTab(new TabState { Path = @"C:\New" });

        Assert.HasCount(3, pane.Tabs);
        Assert.AreEqual(@"C:\New", pane.Tabs[1].Path, "新标签应插在活动标签之后。");
        Assert.AreSame(added, pane.ActiveTab);
    }

    [TestMethod]
    public void CloseTab_LeftOfActive_ShiftsActiveIndexDown()
    {
        PaneState pane = MakePane(@"C:\A", @"C:\B", @"C:\C");
        pane.Activate(2);
        string expected = pane.ActiveTab.Path;

        Assert.IsTrue(pane.CloseTab(0));

        Assert.AreEqual(1, pane.ActiveTabIndex);
        Assert.AreEqual(expected, pane.ActiveTab.Path, "关闭左侧标签后仍应指向同一个标签。");
    }

    [TestMethod]
    public void CloseTab_RightOfActive_KeepsActiveIndex()
    {
        PaneState pane = MakePane(@"C:\A", @"C:\B", @"C:\C");
        pane.Activate(0);

        Assert.IsTrue(pane.CloseTab(2));

        Assert.AreEqual(0, pane.ActiveTabIndex);
        Assert.AreEqual(@"C:\A", pane.ActiveTab.Path);
    }

    [TestMethod]
    public void CloseTab_ActiveInMiddle_RightNeighbourTakesOver()
    {
        PaneState pane = MakePane(@"C:\A", @"C:\B", @"C:\C");
        pane.Activate(1);

        Assert.IsTrue(pane.CloseTab(1));

        Assert.AreEqual(1, pane.ActiveTabIndex);
        Assert.AreEqual(@"C:\C", pane.ActiveTab.Path, "关闭活动标签后应由右邻接替。");
    }

    [TestMethod]
    public void CloseTab_ActiveLast_FallsBackToNewLast()
    {
        PaneState pane = MakePane(@"C:\A", @"C:\B");
        pane.Activate(1);

        Assert.IsTrue(pane.CloseTab(1));

        Assert.AreEqual(0, pane.ActiveTabIndex);
        Assert.AreEqual(@"C:\A", pane.ActiveTab.Path);
    }

    [TestMethod]
    public void CloseTab_LastRemaining_IsRefused()
    {
        PaneState pane = MakePane(@"C:\Only");

        Assert.IsFalse(pane.CloseTab(0), "栏必须至少保留一个标签。");
        Assert.HasCount(1, pane.Tabs);
    }

    [TestMethod]
    public void CloseTab_OutOfRange_IsRefused()
    {
        PaneState pane = MakePane(@"C:\A", @"C:\B");

        Assert.IsFalse(pane.CloseTab(-1));
        Assert.IsFalse(pane.CloseTab(99));
        Assert.HasCount(2, pane.Tabs);
    }

    [TestMethod]
    public void Activate_RejectsOutOfRange()
    {
        PaneState pane = MakePane(@"C:\A");

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => pane.Activate(1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => pane.Activate(-1));
    }

    [TestMethod]
    public void ActiveTab_ClampsStaleIndex()
    {
        PaneState pane = MakePane(@"C:\A", @"C:\B");
        pane.ActiveTabIndex = 99;

        // 外部直接改索引不应崩溃
        Assert.AreEqual(@"C:\B", pane.ActiveTab.Path);
    }

    [TestMethod]
    public void TryDetachActiveTab_WithSiblings_Succeeds()
    {
        PaneState pane = MakePane(@"C:\A", @"C:\B");
        pane.Activate(0);

        Assert.IsTrue(pane.TryDetachActiveTab(out TabState? detached));
        Assert.AreEqual(@"C:\A", detached!.Path);
        Assert.HasCount(1, pane.Tabs);
    }

    [TestMethod]
    public void TryDetachActiveTab_LastTab_IsRefused()
    {
        PaneState pane = MakePane(@"C:\Only");

        Assert.IsFalse(pane.TryDetachActiveTab(out TabState? detached));
        Assert.IsNull(detached);
        Assert.HasCount(1, pane.Tabs);
    }
}
