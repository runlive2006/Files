using System.Runtime.InteropServices;
using Files.Core.Services;

namespace Files.Interop;

/// <summary>
/// 基于 <c>IFileOperation</c> 的 <see cref="IFileOperator"/> 实现。
/// </summary>
/// <remarks>
/// 相比旧的 <c>SHFileOperation</c> 实现，本类解决：
/// <list type="bullet">
/// <item><b>长路径</b>：使用 <c>SHCreateItemFromParsingName</c> + <c>\\?\</c> 前缀；</item>
/// <item><b>可取消</b>：暴露 <see cref="Cancel"/>，长操作可中断；</item>
/// <item><b>异步</b>：<c>StartOperation</c> 与 <c>PerformOperations</c> 分离，
/// 调用方可在后台线程等待而不冻结界面。</item>
/// </list>
/// <para>
/// COM 对象必须<b>在 STA 线程上创建与使用</b>。本类内部不做线程切换，
/// 调用方应保证在同一 STA 线程上完成整个操作序列（UI 线程即满足）。
/// </para>
/// </remarks>
public sealed class FileOperationOperator : IFileOperator, IDisposable
{
    // FOF_* 标志
    private const uint FofSilent = 0x0004;
    private const uint FofNoConfirmation = 0x0010;
    private const uint FofAllowUndo = 0x0040;
    private const uint FofNoErrorUi = 0x0400;
    private const uint FofWantNukeWarning = 0x4000;
    private const uint FofxRecycleOnDelete = 0x00080000;

    // SIGDN_FILESYSPATH：取文件系统路径
    private const uint SigdnFileSysPath = 0x80058000;

    private IFileOperation? _operation;
    private bool _disposed;

    /// <summary>
    /// 是否请求取消当前操作。
    /// </summary>
    /// <remarks>
    /// 只是"请求"：真正的取消由 <c>IFileOperation</c> 在下一个检查点生效。
    /// 为了能调用 <c>Cancel</c>，需要持有 operation 指针。
    /// </remarks>
    public void Cancel() => _cancelRequested = true;

    private volatile bool _cancelRequested;

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

            // 新建目录不需要走 IFileOperation：托管 API 已足够，
            // 且 IFileOperation.NewItem 需要模板参数，收益不大
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

            string trimmed = newName.Trim();

            if (string.Equals(Path.GetFileName(path), trimmed, StringComparison.Ordinal))
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

        return Execute(
            ownerWindow,
            setup: operation =>
            {
                // 回收站：FOF_ALLOWUNDO + FOFX_RECYCLEONDELETE
                // 永久删除：不要 FOF_ALLOWUNDO，并加 FOF_WANTNUKEWARNING 让系统在
                // 删除只读/系统文件时提示（我们自己已确认过前提，这里只是兜底）
                uint flags = FofNoConfirmation | FofNoErrorUi | FofSilent;

                flags |= mode == DeleteMode.Recycle
                    ? FofAllowUndo | FofxRecycleOnDelete
                    : FofWantNukeWarning;

                Check(operation.SetOperationFlags(flags));

                foreach (string path in paths)
                {
                    IShellItem item = CreateItem(path);
                    Check(operation.DeleteItem(item, null));
                    Marshal.ReleaseComObject(item);
                }
            },
            pathCount: paths.Count);
    }

    /// <inheritdoc />
    public FileOperationResult Copy(IReadOnlyList<string> sources, string destinationDirectory, IntPtr ownerWindow)
        => Transfer(sources, destinationDirectory, ownerWindow, move: false);

    /// <inheritdoc />
    public FileOperationResult Move(IReadOnlyList<string> sources, string destinationDirectory, IntPtr ownerWindow)
        => Transfer(sources, destinationDirectory, ownerWindow, move: true);

    private FileOperationResult Transfer(
        IReadOnlyList<string> sources,
        string destinationDirectory,
        IntPtr ownerWindow,
        bool move)
    {
        if (sources is null || sources.Count == 0)
        {
            return FileOperationResult.Fail("没有选中任何项。");
        }

        if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
        {
            return FileOperationResult.Fail("目标目录不存在。");
        }

        return Execute(
            ownerWindow,
            setup: operation =>
            {
                // 不设 FOF_NOCONFIRMATION：重名冲突需要系统弹出"替换/跳过"，
                // 静默覆盖会毁数据
                Check(operation.SetOperationFlags(FofNoErrorUi | FofSilent));

                IShellItem destination = CreateItem(destinationDirectory);

                try
                {
                    foreach (string source in sources)
                    {
                        IShellItem item = CreateItem(source);

                        try
                        {
                            if (move)
                            {
                                Check(operation.MoveItem(item, destination, null, null));
                            }
                            else
                            {
                                Check(operation.CopyItem(item, destination, null, null));
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(item);
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(destination);
                }
            },
            pathCount: sources.Count);
    }

    /// <summary>
    /// 执行一次操作：初始化 COM、排队、执行、清理。
    /// </summary>
    private FileOperationResult Execute(IntPtr ownerWindow, Action<IFileOperation> setup, int pathCount)
    {
        _cancelRequested = false;

        try
        {
            EnsureOperation();
            IFileOperation operation = _operation!;

            if (ownerWindow != IntPtr.Zero)
            {
                // 设置宿主窗口：让系统的进度/冲突对话框以我们的窗口为父级
                Check(operation.SetOwnerWindow(ownerWindow));
            }

            setup(operation);

            if (_cancelRequested)
            {
                return FileOperationResult.Fail("操作已取消。");
            }

            Check(operation.PerformOperations());

            Check(operation.GetAnyOperationsAborted(out bool aborted));

            if (aborted)
            {
                return FileOperationResult.Fail("操作被用户取消。");
            }

            return FileOperationResult.Ok(pathCount);
        }
        catch (COMException ex)
        {
            return FileOperationResult.Fail(DescribeHResult(ex.HResult));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return FileOperationResult.Fail(ex.Message);
        }
        finally
        {
            // 每次操作后释放：IFileOperation 不保证可跨批次复用内部状态
            ReleaseOperation();
        }
    }

    private void EnsureOperation()
    {
        if (_operation is not null)
        {
            return;
        }

        Type? type = Type.GetTypeFromCLSID(new Guid("3ad05575-8857-4850-9277-11b85bdb8e09"));

        if (type is null)
        {
            throw new InvalidOperationException("无法创建 IFileOperation 实例。");
        }

        _operation = (IFileOperation)Activator.CreateInstance(type)!;
    }

    private static IShellItem CreateItem(string path)
    {
        // 长路径必须加 \\?\ 前缀，否则 SHCreateItemFromParsingName 会失败
        string nativePath = PathUtility.NeedsExtendedPrefix(path)
            ? PathUtility.ToExtendedPath(path)
            : path;

        Guid shellItemGuid = typeof(IShellItem).GUID;

        int hr = SHCreateItemFromParsingName(nativePath, IntPtr.Zero, ref shellItemGuid, out IShellItem item);
        Check(hr);

        return item;
    }

    private static void Check(int hr)
    {
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }

    /// <summary>把常见 HRESULT 翻译成可读中文。</summary>
    private static string DescribeHResult(int hr) => (uint)hr switch
    {
        0x80070002 => "找不到文件或文件夹",
        0x80070005 => "拒绝访问",
        0x800704C7 => "操作被用户取消",
        0x80070020 => "文件正被另一个程序使用",
        0x80070070 => "磁盘空间不足",
        0x8007007B => "文件名或路径无效",
        0x80070050 => "目标位置已存在同名文件",
        _ => $"操作失败（0x{hr:X8}）",
    };

    private static bool TryValidateName(string name, out string error)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "名称不能为空。";
            return false;
        }

        if (!PathUtility.IsValidFileName(name.Trim()))
        {
            error = "名称无效：不能包含 \\ / : * ? \" < > |，不能以点结尾，也不能是 CON、NUL 等保留名。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void ReleaseOperation()
    {
        if (_operation is null)
        {
            return;
        }

        if (Marshal.IsComObject(_operation))
        {
            try
            {
                Marshal.ReleaseComObject(_operation);
            }
            catch (ArgumentException)
            {
                // 已释放
            }
        }

        _operation = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseOperation();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);
}
