using Files.Core.Models;
using Files.Core.Services;
using Files.Interop;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class Win32DirectoryListerTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilesWin32Tests_" + Guid.NewGuid().ToString("N"));
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
    public void List_ReturnsDirectoriesFirst_ThenFilesByName()
    {
        Directory.CreateDirectory(Path.Combine(_root, "zeta"));
        Directory.CreateDirectory(Path.Combine(_root, "alpha"));
        File.WriteAllText(Path.Combine(_root, "m.txt"), "12345");
        File.WriteAllText(Path.Combine(_root, "b.log"), "x");

        DirectoryListing listing = new Win32DirectoryLister().List(_root);

        Assert.AreEqual(4, listing.Count);
        Assert.AreEqual("alpha", listing.Entries[0].Name);
        Assert.AreEqual("zeta", listing.Entries[1].Name);
        Assert.IsTrue(listing.Entries[0].IsDirectory);
        Assert.AreEqual("b.log", listing.Entries[2].Name);
        Assert.AreEqual("m.txt", listing.Entries[3].Name);
        Assert.AreEqual(5, listing.Entries[3].SizeBytes);
        Assert.AreEqual(".txt", listing.Entries[3].Extension);
    }

    [TestMethod]
    public void List_ProducesSameOrderAsFileSystemLister()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Dir1"));
        Directory.CreateDirectory(Path.Combine(_root, "dir2"));
        File.WriteAllText(Path.Combine(_root, "A.txt"), "a");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "b");

        DirectoryListing win32 = new Win32DirectoryLister().List(_root);
        DirectoryListing managed = new FileSystemDirectoryLister().List(_root);

        CollectionAssert.AreEqual(
            managed.Entries.Select(e => e.Name).ToArray(),
            win32.Entries.Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public void List_MatchesManagedListerOnSizesAndTimes()
    {
        File.WriteAllText(Path.Combine(_root, "one.txt"), "abcdef");

        DirectoryListing win32 = new Win32DirectoryLister().List(_root);
        DirectoryListing managed = new FileSystemDirectoryLister().List(_root);

        Assert.AreEqual(managed.Entries[0].SizeBytes, win32.Entries[0].SizeBytes);

        // 两者都读 UTC 时间，允许 2 秒误差（文件系统时间戳精度差异）
        double deltaSeconds = Math.Abs(
            (managed.Entries[0].LastModified - win32.Entries[0].LastModified).TotalSeconds);
        Assert.IsLessThan(2, deltaSeconds);
    }

    [TestMethod]
    public void GetFullPath_IsCombinedCorrectly()
    {
        File.WriteAllText(Path.Combine(_root, "one.txt"), "x");

        DirectoryListing listing = new Win32DirectoryLister().List(_root);

        Assert.AreEqual(Path.Combine(_root, "one.txt"), listing.GetFullPath(0));
    }

    [TestMethod]
    public void List_EmptyDirectory_ReturnsEmpty()
    {
        Assert.AreEqual(0, new Win32DirectoryLister().List(_root).Count);
    }

    [TestMethod]
    public void List_MissingDirectory_Throws()
    {
        Assert.ThrowsExactly<DirectoryNotFoundException>(
            () => new Win32DirectoryLister().List(Path.Combine(_root, "nope")));
    }

    [TestMethod]
    public void List_BlankPath_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Win32DirectoryLister().List("   "));
    }

    [TestMethod]
    public void List_HiddenFile_IsExcludedByDefault()
    {
        string hidden = Path.Combine(_root, "hidden.txt");
        File.WriteAllText(hidden, "x");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        DirectoryListing listing = new Win32DirectoryLister().List(_root);

        // 默认不显示隐藏项，与资源管理器一致；需要显示时由设置打开 IncludeHidden
        Assert.IsFalse(listing.Entries.Any(e => e.Name == "hidden.txt"));
    }

    [TestMethod]
    public void ReadAll_ReturnsEntriesWithAttributes()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));

        FileEntry[] entries = Win32DirectoryEnumerator.ReadAll(_root);

        Assert.HasCount(1, entries);
        Assert.IsTrue(entries[0].IsDirectory);
        Assert.IsFalse(entries[0].IsReparsePoint);
    }

    [TestMethod]
    public void ReadAll_DoesNotIncludeDotEntries()
    {
        File.WriteAllText(Path.Combine(_root, "f.txt"), "x");

        FileEntry[] entries = Win32DirectoryEnumerator.ReadAll(_root);

        Assert.IsFalse(entries.Any(e => e.Name is "." or ".."));
    }
}
