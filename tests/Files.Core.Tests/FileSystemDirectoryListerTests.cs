using Files.Core.Models;
using Files.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class FileSystemDirectoryListerTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilesTests_" + Guid.NewGuid().ToString("N"));
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
    public void List_ReturnsDirectoriesBeforeFiles()
    {
        Directory.CreateDirectory(Path.Combine(_root, "zFolder"));
        File.WriteAllText(Path.Combine(_root, "aFile.txt"), "hello");

        var lister = new FileSystemDirectoryLister();
        DirectoryListing listing = lister.List(_root);

        Assert.AreEqual(2, listing.Count);
        Assert.IsTrue(listing.Entries[0].IsDirectory, "文件夹应排在文件之前。");
        Assert.AreEqual("zFolder", listing.Entries[0].Name);
        Assert.AreEqual("aFile.txt", listing.Entries[1].Name);
        Assert.AreEqual(5, listing.Entries[1].SizeBytes);
        Assert.AreEqual(".txt", listing.Entries[1].Extension);
    }

    [TestMethod]
    public void List_MissingDirectory_Throws()
    {
        var lister = new FileSystemDirectoryLister();
        Assert.ThrowsExactly<DirectoryNotFoundException>(
            () => lister.List(Path.Combine(_root, "does-not-exist")));
    }

    [TestMethod]
    public void List_BlankPath_Throws()
    {
        var lister = new FileSystemDirectoryLister();
        Assert.ThrowsExactly<ArgumentException>(() => lister.List("  "));
    }

    [TestMethod]
    public void List_DirectoryPath_IsAbsoluteAndHasNoTrailingSeparator()
    {
        DirectoryListing listing = new FileSystemDirectoryLister().List(_root);

        Assert.IsTrue(Path.IsPathFullyQualified(listing.DirectoryPath));
        Assert.DoesNotEndWith(listing.DirectoryPath, Path.DirectorySeparatorChar.ToString());
    }

    [TestMethod]
    public void GetFullPath_CombinesDirectoryAndName()
    {
        File.WriteAllText(Path.Combine(_root, "one.txt"), "x");

        DirectoryListing listing = new FileSystemDirectoryLister().List(_root);

        Assert.AreEqual(Path.Combine(listing.DirectoryPath, "one.txt"), listing.GetFullPath(0));
        Assert.AreEqual(
            Path.Combine(listing.DirectoryPath, "one.txt"),
            listing.GetFullPath(listing.Entries[0]));
    }

    [TestMethod]
    public void FileSystemEntry_IsValueType_ToAvoidPerItemHeapOverhead()
    {
        // 这是内存预算的关键假设（PERF-BASELINE §3.2），被改动时必须显式察觉
        Assert.IsTrue(typeof(FileSystemEntry).IsValueType);
    }
}
