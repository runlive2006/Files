using Files.Core.Models;
using Files.Core.Services;
using Files.Core.Services.Search;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class SearchQueryParserTests
{
    [TestMethod]
    public void Parse_EmptyText_ReturnsEmptyQuery()
    {
        Assert.IsTrue(SearchQueryParser.Parse(null).IsEmpty);
        Assert.IsTrue(SearchQueryParser.Parse("   ").IsEmpty);
        Assert.IsTrue(SearchQueryParser.Parse("").IsEmpty);
    }

    [TestMethod]
    public void Parse_PlainText_BecomesNameContains()
    {
        SearchQuery query = SearchQueryParser.Parse("报告");

        Assert.HasCount(1, query.Predicates);
        Assert.IsInstanceOfType<NameContains>(query.Predicates[0]);
        Assert.AreEqual("报告", ((NameContains)query.Predicates[0]).Text);
    }

    [TestMethod]
    public void Parse_Extension_AddsDotWhenMissing()
    {
        SearchQuery query = SearchQueryParser.Parse("ext:log");

        Assert.IsInstanceOfType<ExtensionIs>(query.Predicates[0]);
        Assert.AreEqual(".log", ((ExtensionIs)query.Predicates[0]).Extension);
    }

    [TestMethod]
    public void Parse_ExtensionWithDot_IsPreserved()
    {
        SearchQuery query = SearchQueryParser.Parse("ext:.png");

        Assert.AreEqual(".png", ((ExtensionIs)query.Predicates[0]).Extension);
    }

    [TestMethod]
    public void Parse_Kind_MapsAliases()
    {
        Assert.AreEqual(EntryKindFilter.Picture, ((KindIs)SearchQueryParser.Parse("kind:image").Predicates[0]).Kind);
        Assert.AreEqual(EntryKindFilter.Folder, ((KindIs)SearchQueryParser.Parse("kind:folder").Predicates[0]).Kind);
        Assert.AreEqual(EntryKindFilter.Audio, ((KindIs)SearchQueryParser.Parse("kind:music").Predicates[0]).Kind);
    }

    [TestMethod]
    public void Parse_SizeGreaterThan_ConvertsUnit()
    {
        var predicate = (SizeInRange)SearchQueryParser.Parse("size:>10mb").Predicates[0];

        Assert.AreEqual(10L * 1024 * 1024, predicate.MinBytes);
        Assert.IsNull(predicate.MaxBytes);
    }

    [TestMethod]
    public void Parse_SizeRange_ParsesBothEnds()
    {
        var predicate = (SizeInRange)SearchQueryParser.Parse("size:1kb..1mb").Predicates[0];

        Assert.AreEqual(1024L, predicate.MinBytes);
        Assert.AreEqual(1024L * 1024, predicate.MaxBytes);
    }

    [TestMethod]
    public void Parse_SizeLessThan()
    {
        var predicate = (SizeInRange)SearchQueryParser.Parse("size:<100kb").Predicates[0];

        Assert.IsNull(predicate.MinBytes);
        Assert.AreEqual(100L * 1024, predicate.MaxBytes);
    }

    [TestMethod]
    public void Parse_Negation_WrapsInnerPredicate()
    {
        var predicate = (Not)SearchQueryParser.Parse("~ext:.tmp").Predicates[0];

        Assert.IsInstanceOfType<ExtensionIs>(predicate.Inner);
    }

    [TestMethod]
    public void Parse_UnknownPrefix_FallsBackToPlainText()
    {
        // 未识别的属性前缀不能静默返回零结果，应按名称子串处理
        SearchQuery query = SearchQueryParser.Parse("foo:bar");

        Assert.HasCount(1, query.Predicates);
        Assert.IsInstanceOfType<NameContains>(query.Predicates[0]);
        Assert.AreEqual("foo:bar", ((NameContains)query.Predicates[0]).Text);
    }

    [TestMethod]
    public void Parse_QuotedPhrase_KeepsSpaces()
    {
        SearchQuery query = SearchQueryParser.Parse("\"项目 周报\"");

        Assert.AreEqual("项目 周报", ((NameContains)query.Predicates[0]).Text);
    }

    [TestMethod]
    public void Parse_MultipleConditions_AllPresent()
    {
        SearchQuery query = SearchQueryParser.Parse("kind:picture size:>1mb ext:.png");

        Assert.HasCount(3, query.Predicates);
    }

    [TestMethod]
    public void Parse_DateRange_RecognizesKeywords()
    {
        Assert.AreEqual(
            RelativeDateRange.ThisWeek,
            ((ModifiedIn)SearchQueryParser.Parse("datemodified:thisweek").Predicates[0]).Range);
    }
}

[TestClass]
public sealed class SearchMatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.FromHours(8));

    private static FileSystemEntry File(string name, long size = 100, DateTimeOffset? modified = null) =>
        new(name, isDirectory: false, sizeBytes: size, lastModified: modified ?? Now.AddDays(-1));

    private static FileSystemEntry Dir(string name) =>
        new(name, isDirectory: true, sizeBytes: 0, lastModified: Now.AddDays(-1));

    private static bool Match(string queryText, FileSystemEntry entry) =>
        new SearchMatcher(SearchQueryParser.Parse(queryText), Now).IsMatch(entry);

    [TestMethod]
    public void EmptyQuery_MatchesEverything()
    {
        Assert.IsTrue(Match("", File("a.txt")));
        Assert.IsTrue(Match("", Dir("folder")));
    }

    [TestMethod]
    public void NameContains_IsCaseInsensitive()
    {
        Assert.IsTrue(Match("REPORT", File("report.pdf")));
    }

    [TestMethod]
    public void Extension_DoesNotMatchDirectories()
    {
        Assert.IsFalse(Match("ext:.txt", Dir("folder.txt")));
    }

    [TestMethod]
    public void KindFolder_MatchesOnlyDirectories()
    {
        Assert.IsTrue(Match("kind:folder", Dir("d")));
        Assert.IsFalse(Match("kind:folder", File("f.txt")));
    }

    [TestMethod]
    public void KindPicture_MatchesKnownImageExtensions()
    {
        Assert.IsTrue(Match("kind:picture", File("photo.PNG")));
        Assert.IsFalse(Match("kind:picture", File("doc.pdf")));
    }

    [TestMethod]
    public void KindDocument_MatchesTextAndOffice()
    {
        Assert.IsTrue(Match("kind:document", File("readme.md")));
        Assert.IsTrue(Match("kind:document", File("a.xlsx")));
    }

    [TestMethod]
    public void SizeRange_FiltersByBytes()
    {
        Assert.IsTrue(Match("size:>1kb", File("big.bin", 2048)));
        Assert.IsFalse(Match("size:>1kb", File("small.bin", 512)));
    }

    [TestMethod]
    public void SizeRange_DoesNotMatchDirectories()
    {
        Assert.IsFalse(Match("size:>0", Dir("folder")));
    }

    [TestMethod]
    public void Negation_InvertsResult()
    {
        Assert.IsTrue(Match("~ext:.tmp", File("a.txt")));
        Assert.IsFalse(Match("~ext:.tmp", File("a.tmp")));
    }

    [TestMethod]
    public void MultipleConditions_RequireAllToMatch()
    {
        Assert.IsTrue(Match("kind:picture size:>1kb", File("p.png", 2048)));
        Assert.IsFalse(Match("kind:picture size:>1kb", File("p.png", 100)));
    }

    [TestMethod]
    public void DateRange_Today_MatchesRecentOnly()
    {
        Assert.IsTrue(Match("datemodified:today", File("new.txt", 1, Now)));
        Assert.IsFalse(Match("datemodified:today", File("old.txt", 1, Now.AddDays(-10))));
    }

    [TestMethod]
    public void DateRange_ThisYear_ExcludesPreviousYear()
    {
        Assert.IsTrue(Match("datemodified:thisyear", File("y.txt", 1, new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.FromHours(8)))));
        Assert.IsFalse(Match("datemodified:thisyear", File("p.txt", 1, new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.FromHours(8)))));
    }
}

[TestClass]
public sealed class EntryFilterTests
{
    private static DirectoryListing MakeListing(params FileSystemEntry[] entries) =>
        new(@"C:\Test", entries);

    [TestMethod]
    public void Apply_EmptyQuery_ReturnsSameInstance()
    {
        DirectoryListing listing = MakeListing(new FileSystemEntry("a.txt", false, 1, DateTimeOffset.Now));

        DirectoryListing result = EntryFilter.Apply(listing, SearchQuery.Empty);

        Assert.AreSame(listing, result, "空查询不应重建结果。");
    }

    [TestMethod]
    public void Apply_FiltersByName()
    {
        DirectoryListing listing = MakeListing(
            new FileSystemEntry("report.txt", false, 1, DateTimeOffset.Now),
            new FileSystemEntry("notes.txt", false, 1, DateTimeOffset.Now));

        DirectoryListing result = EntryFilter.Apply(listing, SearchQueryParser.Parse("report"));

        Assert.HasCount(1, result.Entries);
        Assert.AreEqual("report.txt", result.Entries[0].Name);
    }

    [TestMethod]
    public void Apply_NoMatch_ReturnsEmptyListing()
    {
        DirectoryListing listing = MakeListing(new FileSystemEntry("a.txt", false, 1, DateTimeOffset.Now));

        DirectoryListing result = EntryFilter.Apply(listing, SearchQueryParser.Parse("zzz"));

        Assert.IsEmpty(result.Entries);
    }

    [TestMethod]
    public void Apply_PreservesDirectoryPath()
    {
        DirectoryListing listing = MakeListing(new FileSystemEntry("a.txt", false, 1, DateTimeOffset.Now));

        DirectoryListing result = EntryFilter.Apply(listing, SearchQueryParser.Parse("a"));

        Assert.AreEqual(@"C:\Test", result.DirectoryPath);
    }

    [TestMethod]
    public void Apply_DoesNotMutateOriginal()
    {
        DirectoryListing listing = MakeListing(
            new FileSystemEntry("a.txt", false, 1, DateTimeOffset.Now),
            new FileSystemEntry("b.txt", false, 1, DateTimeOffset.Now));

        _ = EntryFilter.Apply(listing, SearchQueryParser.Parse("a"));

        Assert.HasCount(2, listing.Entries, "原列举结果不应被修改。");
    }

    [TestMethod]
    public void Apply_CombinedWithSorting_BothHold()
    {
        DirectoryListing listing = MakeListing(
            new FileSystemEntry("z.png", false, 2048, DateTimeOffset.Now),
            new FileSystemEntry("a.png", false, 512, DateTimeOffset.Now),
            new FileSystemEntry("m.txt", false, 1024, DateTimeOffset.Now));

        DirectoryListing filtered = EntryFilter.Apply(listing, SearchQueryParser.Parse("ext:.png"));
        DirectoryListing sorted = EntrySorter.Sort(filtered, EntrySortColumn.Name, descending: false);

        Assert.HasCount(2, sorted.Entries);
        Assert.AreEqual("a.png", sorted.Entries[0].Name);
        Assert.AreEqual("z.png", sorted.Entries[1].Name);
    }
}
