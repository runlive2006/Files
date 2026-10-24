// 直接验证 FileOperationOperator 的 COM 实现是否正确。
// 绕开 UI 键鼠模拟，确保测的是 IFileOperation 本身。
// 用法：dotnet run --project tools\OpCheck -- <返回码>

using Files.Core.Services;
using Files.Interop;

var baseDir = Path.Combine(Path.GetTempPath(), "OpCheck_" + Guid.NewGuid().ToString("N")[..8]);
var srcDir = Path.Combine(baseDir, "src");
var dstDir = Path.Combine(baseDir, "dst");

Directory.CreateDirectory(srcDir);
Directory.CreateDirectory(dstDir);

int failures = 0;

void Check(string label, bool ok, string? detail = null)
{
    if (ok)
    {
        Console.WriteLine($"  [OK]   {label}");
    }
    else
    {
        failures++;
        Console.WriteLine($"  [FAIL] {label}" + (detail is null ? "" : $"  ({detail})"));
    }
}

try
{
    using var op = new FileOperationOperator();

    // ---------- 复制 ----------
    Console.WriteLine("复制：");
    string copySource = Path.Combine(srcDir, "copy-me.txt");
    File.WriteAllText(copySource, "hello");

    FileOperationResult copyResult = op.Copy([copySource], dstDir, IntPtr.Zero);
    Check("Copy 返回成功", copyResult.Success, copyResult.ErrorMessage);
    Check("源文件仍存在", File.Exists(copySource));
    Check("目标文件已生成", File.Exists(Path.Combine(dstDir, "copy-me.txt")));

    // ---------- 移动 ----------
    Console.WriteLine("移动：");
    string moveSource = Path.Combine(srcDir, "move-me.txt");
    File.WriteAllText(moveSource, "world");

    FileOperationResult moveResult = op.Move([moveSource], dstDir, IntPtr.Zero);
    Check("Move 返回成功", moveResult.Success, moveResult.ErrorMessage);
    Check("源文件已消失", !File.Exists(moveSource));
    Check("目标文件已生成", File.Exists(Path.Combine(dstDir, "move-me.txt")));

    // ---------- 删除到回收站 ----------
    Console.WriteLine("删除到回收站：");
    string recycleTarget = Path.Combine(srcDir, "recycle-me.txt");
    File.WriteAllText(recycleTarget, "bye");

    FileOperationResult recycleResult = op.Delete([recycleTarget], DeleteMode.Recycle, IntPtr.Zero);
    Check("Delete(Recycle) 返回成功", recycleResult.Success, recycleResult.ErrorMessage);
    Check("原位置已消失", !File.Exists(recycleTarget));

    // ---------- 永久删除 ----------
    Console.WriteLine("永久删除：");
    string permTarget = Path.Combine(srcDir, "perm-me.txt");
    File.WriteAllText(permTarget, "gone");

    FileOperationResult permResult = op.Delete([permTarget], DeleteMode.Permanent, IntPtr.Zero);
    Check("Delete(Permanent) 返回成功", permResult.Success, permResult.ErrorMessage);
    Check("原位置已消失", !File.Exists(permTarget));

    // ---------- 多文件批量 ----------
    Console.WriteLine("批量删除：");
    string a = Path.Combine(srcDir, "a.txt");
    string b = Path.Combine(srcDir, "b.txt");
    File.WriteAllText(a, "1");
    File.WriteAllText(b, "2");

    FileOperationResult batchResult = op.Delete([a, b], DeleteMode.Permanent, IntPtr.Zero);
    Check("批量 Delete 返回成功", batchResult.Success, batchResult.ErrorMessage);
    Check("两个文件都已删除", !File.Exists(a) && !File.Exists(b));

    // ---------- 目标不存在 ----------
    Console.WriteLine("异常路径：");
    string orphan = Path.Combine(srcDir, "orphan.txt");
    File.WriteAllText(orphan, "x");

    FileOperationResult badDest = op.Copy([orphan], Path.Combine(baseDir, "no-such-dir"), IntPtr.Zero);
    Check("复制到不存在目录应失败", !badDest.Success);
    Check("失败时源文件未受影响", File.Exists(orphan));

    // ---------- 空列表 ----------
    FileOperationResult empty = op.Delete([], DeleteMode.Recycle, IntPtr.Zero);
    Check("空列表应失败", !empty.Success);
}
finally
{
    try
    {
        Directory.Delete(baseDir, recursive: true);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.WriteLine($"  清理失败：{ex.Message}");
    }
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "RESULT=ALL_PASSED" : $"RESULT=FAILURES:{failures}");
return failures == 0 ? 0 : 1;
