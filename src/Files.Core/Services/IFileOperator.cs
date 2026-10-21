namespace Files.Core.Services;

/// <summary>删除方式。</summary>
public enum DeleteMode
{
    /// <summary>移到回收站（可撤销）。默认。</summary>
    Recycle,

    /// <summary>永久删除，不进回收站。</summary>
    Permanent,
}

/// <summary>
/// 一次文件操作的结果。
/// </summary>
public sealed class FileOperationResult
{
    private FileOperationResult(bool success, int affectedCount, string? errorMessage, string? resultPath)
    {
        Success = success;
        AffectedCount = affectedCount;
        ErrorMessage = errorMessage;
        ResultPath = resultPath;
    }

    /// <summary>是否成功。</summary>
    public bool Success { get; }

    /// <summary>受影响的条目数。</summary>
    public int AffectedCount { get; }

    /// <summary>失败原因（已本地化）；成功时为 <see langword="null"/>。</summary>
    public string? ErrorMessage { get; }

    /// <summary>新建或重命名后的完整路径；其他操作为 <see langword="null"/>。</summary>
    public string? ResultPath { get; }

    /// <summary>构造成功结果。</summary>
    public static FileOperationResult Ok(int affectedCount = 1, string? resultPath = null) =>
        new(true, affectedCount, null, resultPath);

    /// <summary>构造失败结果。</summary>
    public static FileOperationResult Fail(string message) =>
        new(false, 0, message, null);
}

/// <summary>
/// 文件操作能力。UI 只依赖此接口，因此可替换实现（M3 换 <c>IFileOperation</c>）。
/// </summary>
public interface IFileOperator
{
    /// <summary>在指定目录下新建文件夹。</summary>
    FileOperationResult CreateDirectory(string parentPath, string name);

    /// <summary>在指定目录下新建空文件。</summary>
    FileOperationResult CreateFile(string parentPath, string name);

    /// <summary>重命名（同目录内改名）。</summary>
    FileOperationResult Rename(string path, string newName);

    /// <summary>删除多个条目。</summary>
    /// <param name="paths">要删除的完整路径。</param>
    /// <param name="mode">删除方式。</param>
    /// <param name="ownerWindow">用于承载系统进度/确认对话框的窗口句柄。</param>
    FileOperationResult Delete(IReadOnlyList<string> paths, DeleteMode mode, IntPtr ownerWindow);

    /// <summary>复制多个条目到目标目录。</summary>
    FileOperationResult Copy(IReadOnlyList<string> sources, string destinationDirectory, IntPtr ownerWindow);

    /// <summary>移动多个条目到目标目录。</summary>
    FileOperationResult Move(IReadOnlyList<string> sources, string destinationDirectory, IntPtr ownerWindow);
}
