using Files.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class NavigationHistoryTests
{
    [TestMethod]
    public void NewHistory_HasNoCurrentAndCannotNavigateBackOrForward()
    {
        var history = new NavigationHistory();

        Assert.IsNull(history.Current);
        Assert.IsFalse(history.CanGoBack);
        Assert.IsFalse(history.CanGoForward);
    }

    [TestMethod]
    public void Navigate_SetsCurrentWithoutBackEntryOnFirstCall()
    {
        var history = new NavigationHistory();

        history.Navigate(@"C:\A");

        Assert.AreEqual(@"C:\A", history.Current);
        Assert.IsFalse(history.CanGoBack);
    }

    [TestMethod]
    public void Navigate_Twice_PushesPreviousToBackStack()
    {
        var history = new NavigationHistory();
        history.Navigate(@"C:\A");
        history.Navigate(@"C:\B");

        Assert.AreEqual(@"C:\B", history.Current);
        Assert.IsTrue(history.CanGoBack);
        Assert.AreEqual(@"C:\A", history.BackStack[^1]);
    }

    [TestMethod]
    public void Navigate_ToSamePath_DoesNotCreateHistoryEntry()
    {
        var history = new NavigationHistory();
        history.Navigate(@"C:\A");
        history.Navigate(@"C:\A");

        Assert.IsFalse(history.CanGoBack);
    }

    [TestMethod]
    public void Back_ReturnsPreviousAndEnablesForward()
    {
        var history = new NavigationHistory();
        history.Navigate(@"C:\A");
        history.Navigate(@"C:\B");

        string? target = history.Back();

        Assert.AreEqual(@"C:\A", target);
        Assert.AreEqual(@"C:\A", history.Current);
        Assert.IsTrue(history.CanGoForward);
        Assert.IsFalse(history.CanGoBack);
    }

    [TestMethod]
    public void Forward_ReturnsNextAndRestoresBack()
    {
        var history = new NavigationHistory();
        history.Navigate(@"C:\A");
        history.Navigate(@"C:\B");
        history.Back();

        string? target = history.Forward();

        Assert.AreEqual(@"C:\B", target);
        Assert.AreEqual(@"C:\B", history.Current);
        Assert.IsTrue(history.CanGoBack);
    }

    [TestMethod]
    public void Navigate_AfterBack_ClearsForwardStack()
    {
        var history = new NavigationHistory();
        history.Navigate(@"C:\A");
        history.Navigate(@"C:\B");
        history.Back();

        history.Navigate(@"C:\C");

        Assert.IsFalse(history.CanGoForward);
        Assert.AreEqual(@"C:\C", history.Current);
    }

    [TestMethod]
    public void Back_WhenEmpty_ReturnsNull()
    {
        var history = new NavigationHistory();
        history.Navigate(@"C:\A");

        Assert.IsNull(history.Back());
        Assert.AreEqual(@"C:\A", history.Current);
    }

    [TestMethod]
    public void Forward_WhenEmpty_ReturnsNull()
    {
        var history = new NavigationHistory();
        history.Navigate(@"C:\A");

        Assert.IsNull(history.Forward());
    }

    [TestMethod]
    public void History_RespectsDepthLimit()
    {
        var history = new NavigationHistory(depth: 3);

        for (int i = 0; i < 10; i++)
        {
            history.Navigate($@"C:\dir{i}");
        }

        Assert.HasCount(3, history.BackStack);
    }

    [TestMethod]
    public void Clear_RemovesEverything()
    {
        var history = new NavigationHistory();
        history.Navigate(@"C:\A");
        history.Navigate(@"C:\B");

        history.Clear();

        Assert.IsNull(history.Current);
        Assert.IsFalse(history.CanGoBack);
        Assert.IsFalse(history.CanGoForward);
    }

    [TestMethod]
    public void Constructor_RejectsNonPositiveDepth()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new NavigationHistory(0));
    }
}
