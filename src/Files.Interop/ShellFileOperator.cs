using Files.Core.Services;

namespace Files.Interop;

/// <summary>
/// 基于 Shell（<c>SHFileOperation</c>）的 <see cref="IFileOperator"/> 实现。
/// </summary>
/// <remarks>
/// M1 占位实现。M3 会替换为 <c>IFileOperation</c>，以获得：
/// 长路径支持、聚合式自定义进度/冲突对话框、可取消的长操作。
/// 当前版本已具备正确的关键语义：删除进回收站、跨卷降级、冲突交给系统处理。
/// </remarks>
public sealed class ShellFileOperator : IFileOperator
{
    /// <inheritdoc />
    public FileOperationResult CreateDirectory(string parentPath, string name)
    {
        if (!TryValidateName(name, out string error))
        {
            return FileOperationResult.Fail(error);
        }

        try
        {
            string trimmed = name.Trim();
            string fullPath = Path.Combine(parentPath, trimmed);
            if (UniqueNameGenerator.Exists(parentPath, trimmed))
            {
                return FileOperationResult.Fail($"已存在同名项：{trimmed}");
            }

            Directory.CreateDirectory(fullPath);
            return FileOperationResult.Ok(resultPath: fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return FileOperationResult.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public FileOperationResult CreateFile(string parentPath, string name)
    {
        if (!TryValidateName(name, out string error))
        {
            return FileOperationResult.Fail(error);
        }

        try
        {
            string trimmed = name.Trim();
            string fullPath = Path.Combine(parentPath, trimmed);
            if (UniqueNameGenerator.Exists(parentPath, trimmed))
            {
                return FileOperationResult.Fail($"已存在同名项：{trimmed}");
            }

            // CreateNew：已存在时抛异常，避免覆盖用户数据
            using (new FileStream(fullPath, FileMode.CreateNew)) { }

            return FileOperationResult.Ok(resultPath: fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return FileOperationResult.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public FileOperationResult Rename(string path, string newName)
    {
        if (!TryValidateName(newName, out string error))
        {
            return FileOperationResult.Fail(error);
        }

        try
        {
            string? parent = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent))
            {
                return FileOperationResult.Fail("无法重命名根目录。");
            }

            string currentName = Path.GetFileName(path);
            string trimmed = newName.Trim();

            if (string.Equals(currentName, trimmed, StringComparison.Ordinal))
            {
                return FileOperationResult.Ok(resultPath: path);
            }

            string target = Path.Combine(parent, trimmed);
            if (UniqueNameGenerator.Exists(parent, trimmed))
            {
                return FileOperationResult.Fail($"已存在同名项：{trimmed}");
            }

            if (Directory.Exists(path))
            {
                Directory.Move(path, target);
            }
            else
            {
                File.Move(path, target);
            }

            return FileOperationResult.Ok(resultPath: target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return FileOperationResult.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public FileOperationResult Delete(IReadOnlyList<string> paths, DeleteMode mode, IntPtr ownerWindow)
    {
        if (paths is null || paths.Count == 0)
        {
            return FileOperationResult.Fail("没有选中任何项。");
        }

        ushort flags = ShellFileOperation.FofNoConfirmation | ShellFileOperation.FofNoErrorUi;

        // Recycle 时加上 FOF_ALLOWUNDO，这是"进回收站"的唯一开关
        if (mode == DeleteMode.Recycle)
        {
            flags |= ShellFileOperation.FofAllowUndo;
        }

        int code = ShellFileOperation.Execute(
            ShellFileOperation.FoDelete,
            Normalize(paths),
            to: null,
            flags,
            ownerWindow);

        return code == 0
            ? FileOperationResult.Ok(paths.Count)
            : FileOperationResult.Fail(ShellFileOperation.DescribeError(code));
    }

    /// <inheritdoc />
    public FileOperationResult Copy(IReadOnlyList<string> sources, string destinationDirectory, IntPtr ownerWindow)
    {
        return Transfer(ShellFileOperation.FoCopy, sources, destinationDirectory, ownerWindow);
    }

    /// <inheritdoc />
    public FileOperationResult Move(IReadOnlyList<string> sources, string destinationDirectory, IntPtr ownerWindow)
    {
        return Transfer(ShellFileOperation.FoMove, sources, destinationDirectory, ownerWindow);
    }

    private static FileOperationResult Transfer(
        uint function,
        IReadOnlyList<string> sources,
        string destinationDirectory,
        IntPtr ownerWindow)
    {
        if (sources is null || sources.Count == 0)
        {
            return FileOperationResult.Fail("没有选中任何项。");
        }

        if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
        {
            return FileOperationResult.Fail("目标目录不存在。");
        }

        // 不设 FOF_NOCONFIRMATION：重名冲突需要系统弹出"替换/跳过"，静默覆盖会毁数据
        int code = ShellFileOperation.Execute(
            function,
            Normalize(sources),
            Path.GetFullPath(destinationDirectory),
            ShellFileOperation.FofNoConfirmMkDir,
            ownerWindow);

        return code == 0
            ? FileOperationResult.Ok(sources.Count)
            : FileOperationResult.Fail(ShellFileOperation.DescribeError(code));
    }

    private static string[] Normalize(IReadOnlyList<string> paths)
    {
        var result = new string[paths.Count];
        for (int i = 0; i < paths.Count; i++)
        {
            result[i] = Path.GetFullPath(paths[i]);
        }

        return result;
    }

    private static bool TryValidateName(string name, out string error)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "名称不能为空。";
            return false;
        }

        // 首尾空白静默去掉（与资源管理器一致）；但尾随的"点"必须拒绝，
        // 因为 Win32 会把 "a." 截成 "a"，属于会真的丢字符的输入。
        if (!PathUtility.IsValidFileName(name.Trim()))
        {
            error = "名称无效：不能包含 \\ / : * ? \" < > |，不能以点结尾，也不能是 CON、NUL 等保留名。";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
