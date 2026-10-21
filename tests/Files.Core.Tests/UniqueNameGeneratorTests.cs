using Files.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class UniqueNameGeneratorTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilesNameTests_" + Guid.NewGuid().ToString("N"));
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
    public void Generate_WhenFree_ReturnsBaseName()
    {
        Assert.AreEqual("新建文件夹", UniqueNameGenerator.Generate(_root, "新建文件夹"));
    }

    [TestMethod]
    public void Generate_WhenTaken_AppendsSequenceStartingAtTwo()
    {
        Directory.CreateDirectory(Path.Combine(_root, "新建文件夹"));

        Assert.AreEqual("新建文件夹 (2)", UniqueNameGenerator.Generate(_root, "新建文件夹"));
    }

    [TestMethod]
    public void Generate_WhenSeveralTaken_SkipsToFirstFree()
    {
        Directory.CreateDirectory(Path.Combine(_root, "新建文件夹"));
        Directory.CreateDirectory(Path.Combine(_root, "新建文件夹 (2)"));
        Directory.CreateDirectory(Path.Combine(_root, "新建文件夹 (3)"));

        Assert.AreEqual("新建文件夹 (4)", UniqueNameGenerator.Generate(_root, "新建文件夹"));
    }

    [TestMethod]
    public void Generate_WithExtension_KeepsExtensionOutsideSequence()
    {
        File.WriteAllText(Path.Combine(_root, "新建文本文档.txt"), "");

        // 序号必须插在扩展名之前，而不是变成 "新建文本文档.txt (2)"
        Assert.AreEqual("新建文本文档 (2).txt", UniqueNameGenerator.Generate(_root, "新建文本文档", ".txt"));
    }

    [TestMethod]
    public void Generate_AcceptsExtensionWithoutDot()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "");

        Assert.AreEqual("a (2).txt", UniqueNameGenerator.Generate(_root, "a", "txt"));
    }

    [TestMethod]
    public void Exists_DetectsFiles()
    {
        File.WriteAllText(Path.Combine(_root, "x.txt"), "");

        Assert.IsTrue(UniqueNameGenerator.Exists(_root, "x.txt"));
        Assert.IsFalse(UniqueNameGenerator.Exists(_root, "y.txt"));
    }

    [TestMethod]
    public void Exists_DetectsDirectories()
    {
        // 只查 File.Exists 会漏掉同名目录，导致后续新建抛异常
        Directory.CreateDirectory(Path.Combine(_root, "somedir"));

        Assert.IsTrue(UniqueNameGenerator.Exists(_root, "somedir"));
    }

    [TestMethod]
    public void Generate_RejectsBlankArguments()
    {
        Assert.ThrowsExactly<ArgumentException>(() => UniqueNameGenerator.Generate("  ", "a"));
        Assert.ThrowsExactly<ArgumentException>(() => UniqueNameGenerator.Generate(_root, "  "));
    }
}
