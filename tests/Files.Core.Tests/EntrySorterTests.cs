using Files.Core.Models;
using Files.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class EntrySorterTests
{
    private static DirectoryListing MakeListing(params (string Name, bool IsDir, long Size, int Day)[] items)
    {
        var entries = items
            .Select(i => new FileSystemEntry(
                i.Name,
                i.IsDir,
                i.Size,
                new DateTimeOffset(2026, 1, i.Day, 0, 0, 0, TimeSpan.Zero)))
            .ToArray();

        return new DirectoryListing(@"C:\Test", entries);
    }

    private static string[] Names(DirectoryListing listing) =>
        listing.Entries.Select(e => e.Name).ToArray();

    [TestMethod]
    public void SortByName_Ascending_PutsFoldersFirst()
    {
        var listing = MakeListing(
            ("b.txt", false, 10, 1),
            ("zdir", true, 0, 1),
            ("a.txt", false, 10, 1),
            ("adir", true, 0, 1));

        var sorted = EntrySorter.Sort(listing, EntrySortColumn.Name, descending: false);

        CollectionAssert.AreEqual(new[] { "adir", "zdir", "a.txt", "b.txt" }, Names(sorted));
    }

    [TestMethod]
    public void SortByName_Descending_KeepsFoldersFirst()
    {
        var listing = MakeListing(
            ("a.txt", false, 10, 1),
            ("adir", true, 0, 1),
            ("b.txt", false, 10, 1),
            ("zdir", true, 0, 1));

        var sorted = EntrySorter.Sort(listing, EntrySortColumn.Name, descending: true);

        // 降序只作用于组内：文件夹仍在前，但内部名称降序
        CollectionAssert.AreEqual(new[] { "zdir", "adir", "b.txt", "a.txt" }, Names(sorted));
    }

    [TestMethod]
    public void SortBySize_OrdersFilesBySize()
    {
        var listing = MakeListing(
            ("big.bin", false, 5000, 1),
            ("small.bin", false, 10, 1),
            ("mid.bin", false, 500, 1));

        var sorted = EntrySorter.Sort(listing, EntrySortColumn.Size, descending: false);

        CollectionAssert.AreEqual(new[] { "small.bin", "mid.bin", "big.bin" }, Names(sorted));
    }

    [TestMethod]
    public void SortByLastModified_OrdersByTime()
    {
        var listing = MakeListing(
            ("c.txt", false, 1, 20),
            ("a.txt", false, 1, 5),
            ("b.txt", false, 1, 12));

        var sorted = EntrySorter.Sort(listing, EntrySortColumn.LastModified, descending: false);

        CollectionAssert.AreEqual(new[] { "a.txt", "b.txt", "c.txt" }, Names(sorted));
    }

    [TestMethod]
    public void SortByKind_GroupsByExtension()
    {
        var listing = MakeListing(
            ("z.txt", false, 1, 1),
            ("a.png", false, 1, 1),
            ("b.txt", false, 1, 1),
            ("c.png", false, 1, 1));

        var sorted = EntrySorter.Sort(listing, EntrySortColumn.Kind, descending: false);

        // .png 在 .txt 之前；同扩展名内部按名称升序
        CollectionAssert.AreEqual(new[] { "a.png", "c.png", "b.txt", "z.txt" }, Names(sorted));
    }

    [TestMethod]
    public void Sort_DoesNotMutateOriginalListing()
    {
        var listing = MakeListing(("b.txt", false, 1, 1), ("a.txt", false, 1, 1));
        string[] before = Names(listing);

        _ = EntrySorter.Sort(listing, EntrySortColumn.Name, descending: false);

        CollectionAssert.AreEqual(before, Names(listing));
    }

    [TestMethod]
    public void Sort_EmptyListing_ReturnsEmpty()
    {
        var listing = new DirectoryListing(@"C:\Empty", []);
        var sorted = EntrySorter.Sort(listing, EntrySortColumn.Name, descending: false);

        Assert.AreEqual(0, sorted.Count);
    }

    [TestMethod]
    public void Sort_TiesFallBackToNameAscending_EvenWhenDescending()
    {
        var listing = MakeListing(
            ("b.txt", false, 100, 1),
            ("a.txt", false, 100, 1),
            ("c.txt", false, 100, 1));

        var sorted = EntrySorter.Sort(listing, EntrySortColumn.Size, descending: true);

        CollectionAssert.AreEqual(new[] { "a.txt", "b.txt", "c.txt" }, Names(sorted));
    }

    [TestMethod]
    public void Sort_PreservesDirectoryPath()
    {
        var listing = MakeListing(("a.txt", false, 1, 1));
        var sorted = EntrySorter.Sort(listing, EntrySortColumn.Name, descending: false);

        Assert.AreEqual(@"C:\Test", sorted.DirectoryPath);
    }
}
