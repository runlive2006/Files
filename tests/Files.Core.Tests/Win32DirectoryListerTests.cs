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

        var entries = new Win32DirectoryLister().List(_root);

        Assert.HasCount(4, entries);
        Assert.AreEqual("alpha", entries[0].Name);
        Assert.AreEqual("zeta", entries[1].Name);
        Assert.IsTrue(entries[0].IsDirectory);
        Assert.AreEqual("b.log", entries[2].Name);
        Assert.AreEqual("m.txt", entries[3].Name);
        Assert.AreEqual(5, entries[3].SizeBytes);
        Assert.AreEqual(".txt", entries[3].Extension);
    }

    [TestMethod]
    public void List_ProducesSameOrderAsFileSystemLister()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Dir1"));
        Directory.CreateDirectory(Path.Combine(_root, "dir2"));
        File.WriteAllText(Path.Combine(_root, "A.txt"), "a");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "b");

        var win32 = new Win32DirectoryLister().List(_root);
        var managed = new FileSystemDirectoryLister().List(_root);

        CollectionAssert.AreEqual(
            managed.Select(e => e.Name).ToArray(),
            win32.Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public void List_FullPath_IsCombinedCorrectly()
    {
        File.WriteAllText(Path.Combine(_root, "one.txt"), "x");

        var entry = new Win32DirectoryLister().List(_root).Single();

        Assert.AreEqual(Path.Combine(_root, "one.txt"), entry.FullPath);
    }

    [TestMethod]
    public void List_EmptyDirectory_ReturnsEmpty()
    {
        Assert.IsEmpty(new Win32DirectoryLister().List(_root));
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
    public void List_HiddenFile_IsIncluded()
    {
        string hidden = Path.Combine(_root, "hidden.txt");
        File.WriteAllText(hidden, "x");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var entries = new Win32DirectoryLister().List(_root);

        Assert.IsTrue(entries.Any(e => e.Name == "hidden.txt"));
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
