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
        IReadOnlyList<FileSystemEntry> entries = lister.List(_root);

        Assert.HasCount(2, entries);
        Assert.IsTrue(entries[0].IsDirectory, "文件夹应排在文件之前。");
        Assert.AreEqual("zFolder", entries[0].Name);
        Assert.AreEqual("aFile.txt", entries[1].Name);
        Assert.AreEqual(5, entries[1].SizeBytes);
        Assert.AreEqual(".txt", entries[1].Extension);
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
}
