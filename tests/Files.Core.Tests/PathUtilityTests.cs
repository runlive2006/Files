using Files.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class PathUtilityTests
{
    [TestMethod]
    public void Expand_TrimsQuotesAndExpandsVariables()
    {
        string expanded = PathUtility.Expand("  \"%TEMP%\"  ");

        Assert.DoesNotStartWith(expanded, "\"");
        Assert.DoesNotStartWith(expanded, " ");
        Assert.DoesNotEndWith(expanded, "\"");
        Assert.AreNotEqual("%TEMP%", expanded);
    }

    [TestMethod]
    [DataRow("CON")]
    [DataRow("con")]
    [DataRow("CON.txt")]
    [DataRow("NUL.log")]
    [DataRow("COM1")]
    [DataRow("lpt9.tar.gz")]
    public void IsReservedDeviceName_RecognizesReservedNames(string name)
    {
        Assert.IsTrue(PathUtility.IsReservedDeviceName(name));
    }

    [TestMethod]
    [DataRow("CONSOLE")]
    [DataRow("COM10")]
    [DataRow("my-CON")]
    [DataRow("NULl.txt")]
    [DataRow("")]
    public void IsReservedDeviceName_RejectsOrdinaryNames(string name)
    {
        Assert.IsFalse(PathUtility.IsReservedDeviceName(name));
    }

    [TestMethod]
    [DataRow("report.txt")]
    [DataRow("项目 周报.docx")]
    [DataRow("a.b.c")]
    public void IsValidFileName_AcceptsNormalNames(string name)
    {
        Assert.IsTrue(PathUtility.IsValidFileName(name));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(".")]
    [DataRow("..")]
    [DataRow("a<b")]
    [DataRow("a>b")]
    [DataRow("a:b")]
    [DataRow("a\"b")]
    [DataRow("a/b")]
    [DataRow("a\\b")]
    [DataRow("a|b")]
    [DataRow("a?b")]
    [DataRow("a*b")]
    [DataRow("trailing ")]
    [DataRow("trailing.")]
    [DataRow("CON")]
    public void IsValidFileName_RejectsInvalidNames(string name)
    {
        Assert.IsFalse(PathUtility.IsValidFileName(name));
    }

    [TestMethod]
    public void ToExtendedPath_LocalPath_AddsPrefix()
    {
        Assert.AreEqual(@"\\?\C:\Temp\file.txt", PathUtility.ToExtendedPath(@"C:\Temp\file.txt"));
    }

    [TestMethod]
    public void ToExtendedPath_UncPath_UsesUncForm()
    {
        // 直接加 \\?\ 会得到无效的 \\?\\\server\share，必须走 UNC 形式
        Assert.AreEqual(
            @"\\?\UNC\server\share\file.txt",
            PathUtility.ToExtendedPath(@"\\server\share\file.txt"));
    }

    [TestMethod]
    public void ToExtendedPath_AlreadyExtended_IsIdempotent()
    {
        const string path = @"\\?\C:\Temp\file.txt";
        Assert.AreEqual(path, PathUtility.ToExtendedPath(path));
    }

    [TestMethod]
    public void ToExtendedPath_DevicePath_IsUnchanged()
    {
        const string path = @"\\.\PhysicalDrive0";
        Assert.AreEqual(path, PathUtility.ToExtendedPath(path));
    }

    [TestMethod]
    public void FromExtendedPath_RestoresOriginalForms()
    {
        Assert.AreEqual(@"C:\Temp\a", PathUtility.FromExtendedPath(@"\\?\C:\Temp\a"));
        Assert.AreEqual(@"\\server\share\a", PathUtility.FromExtendedPath(@"\\?\UNC\server\share\a"));
        Assert.AreEqual(@"C:\Temp\a", PathUtility.FromExtendedPath(@"C:\Temp\a"));
    }

    [TestMethod]
    public void NeedsExtendedPrefix_ShortPath_IsFalse()
    {
        Assert.IsFalse(PathUtility.NeedsExtendedPrefix(@"C:\Temp\a.txt"));
    }

    [TestMethod]
    public void NeedsExtendedPrefix_LongPath_IsTrue()
    {
        string longPath = @"C:\Temp\" + new string('a', 260);
        Assert.IsTrue(PathUtility.NeedsExtendedPrefix(longPath));
    }

    [TestMethod]
    public void AreEquivalent_IgnoresCaseSeparatorsAndTrailingSlash()
    {
        Assert.IsTrue(PathUtility.AreEquivalent(@"C:\Work\Src", @"c:/work/src/"));
        Assert.IsFalse(PathUtility.AreEquivalent(@"C:\Work\Src", @"C:\Work\Bin"));
    }

    [TestMethod]
    public void NormalizeForComparison_AppliesUnicodeFormC()
    {
        // "é" 的合成形式与分解形式必须视为同一个名称
        string composed = "caf\u00E9";
        string decomposed = "cafe\u0301";

        Assert.IsTrue(PathUtility.AreEquivalent(composed, decomposed));
    }

    [TestMethod]
    public void EnsureTrailingSeparator_HandlesRootAndDirectory()
    {
        Assert.AreEqual(@"C:\", PathUtility.EnsureTrailingSeparator("C:"));
        Assert.AreEqual(@"C:\Temp\", PathUtility.EnsureTrailingSeparator(@"C:\Temp"));
        Assert.AreEqual(@"C:\Temp\", PathUtility.EnsureTrailingSeparator(@"C:\Temp\"));
    }

    [TestMethod]
    public void SplitSegments_IgnoresEmptyParts()
    {
        string[] segments = PathUtility.SplitSegments(@"C:\a\\b\c\");

        CollectionAssert.AreEqual(new[] { "C:", "a", "b", "c" }, segments);
    }
}
