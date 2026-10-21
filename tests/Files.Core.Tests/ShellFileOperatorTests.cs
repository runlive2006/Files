using Files.Core.Services;
using Files.Interop;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class ShellFileOperatorTests
{
    private string _root = string.Empty;
    private readonly ShellFileOperator _operator = new();

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilesOps_" + Guid.NewGuid().ToString("N"));
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

    // ---------- 新建 ----------

    [TestMethod]
    public void CreateDirectory_CreatesFolder()
    {
        FileOperationResult result = _operator.CreateDirectory(_root, "新文件夹");

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsTrue(Directory.Exists(Path.Combine(_root, "新文件夹")));
        Assert.AreEqual(Path.Combine(_root, "新文件夹"), result.ResultPath);
    }

    [TestMethod]
    public void CreateDirectory_WhenNameTaken_Fails()
    {
        Directory.CreateDirectory(Path.Combine(_root, "dup"));

        FileOperationResult result = _operator.CreateDirectory(_root, "dup");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ErrorMessage, "已存在同名项");
    }

    [TestMethod]
    public void CreateFile_CreatesEmptyFile()
    {
        FileOperationResult result = _operator.CreateFile(_root, "新建文本文档.txt");

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreEqual(0, new FileInfo(Path.Combine(_root, "新建文本文档.txt")).Length);
    }

    [TestMethod]
    public void CreateFile_WhenNameTaken_DoesNotOverwrite()
    {
        string path = Path.Combine(_root, "data.txt");
        File.WriteAllText(path, "原始内容");

        FileOperationResult result = _operator.CreateFile(_root, "data.txt");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("原始内容", File.ReadAllText(path), "同名文件内容不得被覆盖。");
    }

    [TestMethod]
    [DataRow("CON")]
    [DataRow("a<b")]
    [DataRow("trailing.")]
    [DataRow("bad|name")]
    public void CreateDirectory_RejectsInvalidNames(string name)
    {
        FileOperationResult result = _operator.CreateDirectory(_root, name);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ErrorMessage, "名称无效");
    }

    [TestMethod]
    public void CreateDirectory_TrimsSurroundingWhitespace()
    {
        // 与资源管理器一致：首尾空白被静默去掉，而不是报错
        FileOperationResult result = _operator.CreateDirectory(_root, "  带空格  ");

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsTrue(Directory.Exists(Path.Combine(_root, "带空格")));
    }

    // ---------- 重命名 ----------

    [TestMethod]
    public void Rename_File_Succeeds()
    {
        string source = Path.Combine(_root, "old.txt");
        File.WriteAllText(source, "x");

        FileOperationResult result = _operator.Rename(source, "new.txt");

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsFalse(File.Exists(source));
        Assert.IsTrue(File.Exists(Path.Combine(_root, "new.txt")));
    }

    [TestMethod]
    public void Rename_Directory_Succeeds()
    {
        string source = Path.Combine(_root, "olddir");
        Directory.CreateDirectory(source);

        FileOperationResult result = _operator.Rename(source, "newdir");

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsTrue(Directory.Exists(Path.Combine(_root, "newdir")));
    }

    [TestMethod]
    public void Rename_ToExistingName_Fails()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "b");

        FileOperationResult result = _operator.Rename(Path.Combine(_root, "a.txt"), "b.txt");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("a", File.ReadAllText(Path.Combine(_root, "a.txt")), "失败时不得改动源文件。");
        Assert.AreEqual("b", File.ReadAllText(Path.Combine(_root, "b.txt")), "失败时不得覆盖目标文件。");
    }

    [TestMethod]
    public void Rename_ToSameName_IsNoOpSuccess()
    {
        string source = Path.Combine(_root, "same.txt");
        File.WriteAllText(source, "x");

        FileOperationResult result = _operator.Rename(source, "same.txt");

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(source));
    }

    [TestMethod]
    public void Rename_RejectsInvalidName()
    {
        string source = Path.Combine(_root, "x.txt");
        File.WriteAllText(source, "x");

        FileOperationResult result = _operator.Rename(source, "bad|name");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(File.Exists(source));
    }

    // ---------- 删除 ----------

    [TestMethod]
    public void Delete_Recycle_RemovesFromOriginalLocation()
    {
        string path = Path.Combine(_root, "todelete.txt");
        File.WriteAllText(path, "x");

        FileOperationResult result = _operator.Delete([path], DeleteMode.Recycle, IntPtr.Zero);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public void Delete_Permanent_RemovesFromOriginalLocation()
    {
        string path = Path.Combine(_root, "perm.txt");
        File.WriteAllText(path, "x");

        FileOperationResult result = _operator.Delete([path], DeleteMode.Permanent, IntPtr.Zero);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public void Delete_MultipleItems_RemovesAll()
    {
        string first = Path.Combine(_root, "a.txt");
        string second = Path.Combine(_root, "b.txt");
        File.WriteAllText(first, "a");
        File.WriteAllText(second, "b");

        FileOperationResult result = _operator.Delete([first, second], DeleteMode.Permanent, IntPtr.Zero);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsFalse(File.Exists(first));
        Assert.IsFalse(File.Exists(second));
    }

    [TestMethod]
    public void Delete_Directory_RemovesRecursively()
    {
        string dir = Path.Combine(_root, "dir");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "inner.txt"), "x");

        FileOperationResult result = _operator.Delete([dir], DeleteMode.Permanent, IntPtr.Zero);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsFalse(Directory.Exists(dir));
    }

    [TestMethod]
    public void Delete_EmptyList_Fails()
    {
        FileOperationResult result = _operator.Delete([], DeleteMode.Recycle, IntPtr.Zero);

        Assert.IsFalse(result.Success);
    }

    // ---------- 复制 / 移动 ----------

    [TestMethod]
    public void Copy_CopiesFileIntoDestination()
    {
        string source = Path.Combine(_root, "src.txt");
        File.WriteAllText(source, "hello");
        string destination = Path.Combine(_root, "dest");
        Directory.CreateDirectory(destination);

        FileOperationResult result = _operator.Copy([source], destination, IntPtr.Zero);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsTrue(File.Exists(source), "复制后源文件应保留。");
        Assert.AreEqual("hello", File.ReadAllText(Path.Combine(destination, "src.txt")));
    }

    [TestMethod]
    public void Copy_Directory_CopiesRecursively()
    {
        string source = Path.Combine(_root, "srcdir");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "inner.txt"), "data");
        string destination = Path.Combine(_root, "dest");
        Directory.CreateDirectory(destination);

        FileOperationResult result = _operator.Copy([source], destination, IntPtr.Zero);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreEqual("data", File.ReadAllText(Path.Combine(destination, "srcdir", "inner.txt")));
    }

    [TestMethod]
    public void Move_MovesFileAndRemovesSource()
    {
        string source = Path.Combine(_root, "move.txt");
        File.WriteAllText(source, "hello");
        string destination = Path.Combine(_root, "dest");
        Directory.CreateDirectory(destination);

        FileOperationResult result = _operator.Move([source], destination, IntPtr.Zero);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsFalse(File.Exists(source), "移动后源文件应消失。");
        Assert.AreEqual("hello", File.ReadAllText(Path.Combine(destination, "move.txt")));
    }

    [TestMethod]
    public void Copy_ToMissingDestination_Fails()
    {
        string source = Path.Combine(_root, "src.txt");
        File.WriteAllText(source, "x");

        FileOperationResult result = _operator.Copy([source], Path.Combine(_root, "nope"), IntPtr.Zero);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ErrorMessage, "目标目录不存在");
    }

    [TestMethod]
    public void Copy_EmptySourceList_Fails()
    {
        FileOperationResult result = _operator.Copy([], _root, IntPtr.Zero);

        Assert.IsFalse(result.Success);
    }
}
