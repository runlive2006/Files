using Files.Core.Models;
using Files.Core.Services;
using Files.Interop;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class HiddenEntryFilteringTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilesHidden_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            // 清掉隐藏属性，否则递归删除会失败
            foreach (string file in Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }

            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public void Win32Lister_ExcludesHiddenByDefault()
    {
        File.WriteAllText(Path.Combine(_root, "visible.txt"), "x");
        string hidden = Path.Combine(_root, "hidden.txt");
        File.WriteAllText(hidden, "x");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var lister = new Win32DirectoryLister();
        DirectoryListing listing = lister.List(_root);

        Assert.HasCount(1, listing.Entries);
        Assert.AreEqual("visible.txt", listing.Entries[0].Name);
    }

    [TestMethod]
    public void Win32Lister_IncludesHiddenWhenEnabled()
    {
        File.WriteAllText(Path.Combine(_root, "visible.txt"), "x");
        string hidden = Path.Combine(_root, "hidden.txt");
        File.WriteAllText(hidden, "x");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var lister = new Win32DirectoryLister { IncludeHidden = true };
        DirectoryListing listing = lister.List(_root);

        Assert.HasCount(2, listing.Entries);
        Assert.IsTrue(listing.Entries.Any(e => e.Name == "hidden.txt"));
    }

    [TestMethod]
    public void Win32Lister_MarksHiddenFlag()
    {
        string hidden = Path.Combine(_root, "h.txt");
        File.WriteAllText(hidden, "x");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var lister = new Win32DirectoryLister { IncludeHidden = true };
        DirectoryListing listing = lister.List(_root);

        FileSystemEntry entry = listing.Entries.Single();
        Assert.IsTrue(entry.IsHidden, "隐藏属性应被记录到条目上。");
    }

    [TestMethod]
    public void Win32Lister_TreatsSystemFilesAsHidden()
    {
        string system = Path.Combine(_root, "s.txt");
        File.WriteAllText(system, "x");
        File.SetAttributes(system, FileAttributes.System);

        var lister = new Win32DirectoryLister { IncludeHidden = true };
        DirectoryListing listing = lister.List(_root);

        Assert.IsTrue(listing.Entries.Single().IsHidden, "系统文件应与隐藏文件同等对待。");
    }

    [TestMethod]
    public void ManagedLister_MatchesWin32Behaviour()
    {
        File.WriteAllText(Path.Combine(_root, "visible.txt"), "x");
        string hidden = Path.Combine(_root, "hidden.txt");
        File.WriteAllText(hidden, "x");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        DirectoryListing win32 = new Win32DirectoryLister().List(_root);
        DirectoryListing managed = new FileSystemDirectoryLister().List(_root);

        CollectionAssert.AreEqual(
            managed.Entries.Select(e => e.Name).ToArray(),
            win32.Entries.Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public void ManagedLister_IncludesHiddenWhenEnabled()
    {
        string hidden = Path.Combine(_root, "h.txt");
        File.WriteAllText(hidden, "x");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var lister = new FileSystemDirectoryLister { IncludeHidden = true };
        DirectoryListing listing = lister.List(_root);

        Assert.HasCount(1, listing.Entries);
        Assert.IsTrue(listing.Entries[0].IsHidden);
    }

    [TestMethod]
    public void HiddenDirectory_IsFilteredToo()
    {
        string hiddenDir = Path.Combine(_root, "hiddendir");
        Directory.CreateDirectory(hiddenDir);
        File.SetAttributes(hiddenDir, FileAttributes.Hidden);

        DirectoryListing withoutHidden = new Win32DirectoryLister().List(_root);
        DirectoryListing withHidden = new Win32DirectoryLister { IncludeHidden = true }.List(_root);

        Assert.IsEmpty(withoutHidden.Entries);
        Assert.HasCount(1, withHidden.Entries);
        Assert.IsTrue(withHidden.Entries[0].IsDirectory);
    }
}
