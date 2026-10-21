using Files.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class DropPolicyTests
{
    [TestMethod]
    public void Resolve_ControlPressed_AlwaysCopies()
    {
        DropAction action = DropPolicy.Resolve(
            controlPressed: true,
            shiftPressed: false,
            sourcePath: @"C:\A\file.txt",
            targetDirectory: @"C:\A\Sub");

        Assert.AreEqual(DropAction.Copy, action);
    }

    [TestMethod]
    public void Resolve_ControlWins_OverShift()
    {
        // 两个修饰键同时按下时以 Ctrl 为准，与资源管理器一致
        DropAction action = DropPolicy.Resolve(
            controlPressed: true,
            shiftPressed: true,
            sourcePath: @"C:\A\file.txt",
            targetDirectory: @"C:\A\Sub");

        Assert.AreEqual(DropAction.Copy, action);
    }

    [TestMethod]
    public void Resolve_ShiftPressed_Moves()
    {
        DropAction action = DropPolicy.Resolve(
            controlPressed: false,
            shiftPressed: true,
            sourcePath: @"C:\A\file.txt",
            targetDirectory: @"D:\B");

        Assert.AreEqual(DropAction.Move, action);
    }

    [TestMethod]
    public void Resolve_SameVolumeWithoutModifiers_Moves()
    {
        DropAction action = DropPolicy.Resolve(
            controlPressed: false,
            shiftPressed: false,
            sourcePath: @"C:\Work\file.txt",
            targetDirectory: @"C:\Archive");

        Assert.AreEqual(DropAction.Move, action);
    }

    [TestMethod]
    public void Resolve_CrossVolumeWithoutModifiers_Copies()
    {
        // 跨卷"移动"物理上是复制+删除，明确降级为复制更符合预期
        DropAction action = DropPolicy.Resolve(
            controlPressed: false,
            shiftPressed: false,
            sourcePath: @"C:\Work\file.txt",
            targetDirectory: @"D:\Archive");

        Assert.AreEqual(DropAction.Copy, action);
    }

    [TestMethod]
    public void SameVolume_SameDrive_IsTrue()
    {
        Assert.IsTrue(DropPolicy.SameVolume(@"C:\A\file.txt", @"C:\B"));
    }

    [TestMethod]
    public void SameVolume_DifferentDrive_IsFalse()
    {
        Assert.IsFalse(DropPolicy.SameVolume(@"C:\A\file.txt", @"D:\B"));
    }

    [TestMethod]
    public void SameVolume_IsCaseInsensitive()
    {
        Assert.IsTrue(DropPolicy.SameVolume(@"c:\A\file.txt", @"C:\B"));
    }

    [TestMethod]
    public void SameVolume_DifferentUncShares_IsFalse()
    {
        Assert.IsFalse(DropPolicy.SameVolume(@"\\server\share1\file.txt", @"\\server\share2"));
    }

    [TestMethod]
    public void SameVolume_BlankPath_IsFalse()
    {
        Assert.IsFalse(DropPolicy.SameVolume(string.Empty, @"C:\B"));
        Assert.IsFalse(DropPolicy.SameVolume(@"C:\A", "  "));
    }
}
