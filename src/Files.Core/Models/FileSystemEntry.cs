namespace Files.Core.Models;

/// <summary>
/// 表示文件系统中的一个条目（文件或文件夹）。
/// </summary>
public sealed class FileSystemEntry
{
    /// <summary>条目的完整路径。</summary>
    public required string FullPath { get; init; }

    /// <summary>条目在所在目录中的显示名称。</summary>
    public required string Name { get; init; }

    /// <summary>是否为文件夹。</summary>
    public required bool IsDirectory { get; init; }

    /// <summary>文件大小（字节）；文件夹为 0。</summary>
    public long SizeBytes { get; init; }

    /// <summary>最后修改时间。</summary>
    public DateTimeOffset LastModified { get; init; }

    /// <summary>扩展名（含点），文件夹为空字符串。</summary>
    public string Extension => IsDirectory ? string.Empty : Path.GetExtension(Name);
}
