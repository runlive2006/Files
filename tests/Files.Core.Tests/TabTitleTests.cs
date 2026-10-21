using Files.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class TabTitleTests
{
    private static TabState Tab(string path) => new() { Path = path };

    [TestMethod]
    public void Resolve_UniqueName_ShowsShortNameOnly()
    {
        TabState tab = Tab(@"C:\Work\Project");

        Assert.AreEqual("Project", TabTitle.Resolve(tab, [tab]));
    }

    [TestMethod]
    public void Resolve_DuplicateNames_AddsParentDirectory()
    {
        TabState first = Tab(@"C:\Work\src");
        TabState second = Tab(@"C:\Backup\src");

        Assert.AreEqual(@"Work\src", TabTitle.Resolve(first, [first, second]));
        Assert.AreEqual(@"Backup\src", TabTitle.Resolve(second, [first, second]));
    }

    [TestMethod]
    public void Resolve_DuplicateDetectionIsCaseInsensitive()
    {
        TabState first = Tab(@"C:\A\SRC");
        TabState second = Tab(@"C:\B\src");

        StringAssert.Contains(TabTitle.Resolve(first, [first, second]), @"A\");
    }

    [TestMethod]
    public void Resolve_DriveRoot_ShowsDrive()
    {
        TabState tab = Tab(@"C:\");

        Assert.AreEqual(@"C:", TabTitle.Resolve(tab, [tab]));
    }

    [TestMethod]
    public void GetShortName_TrailingSeparator_IsIgnored()
    {
        Assert.AreEqual("Project", TabTitle.GetShortName(@"C:\Work\Project\"));
    }

    [TestMethod]
    public void GetShortName_EmptyPath_FallsBack()
    {
        Assert.AreEqual("此电脑", TabTitle.GetShortName(string.Empty));
    }

    [TestMethod]
    public void GetParentName_ReturnsParentSegment()
    {
        Assert.AreEqual("Work", TabTitle.GetParentName(@"C:\Work\Project"));
    }

    [TestMethod]
    public void GetParentName_ForDriveRoot_IsEmpty()
    {
        Assert.AreEqual(string.Empty, TabTitle.GetParentName(@"C:\"));
    }

    [TestMethod]
    public void Resolve_RejectsNullArguments()
    {
        TabState tab = Tab(@"C:\A");

        Assert.ThrowsExactly<ArgumentNullException>(() => TabTitle.Resolve(null!, [tab]));
        Assert.ThrowsExactly<ArgumentNullException>(() => TabTitle.Resolve(tab, null!));
    }
}
