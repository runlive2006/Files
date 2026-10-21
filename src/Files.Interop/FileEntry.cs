namespace Files.Interop;

/// <summary>
/// 枚举得到的一个文件系统条目。刻意做成结构体：10 万项时避免 10 万次堆分配。
/// </summary>
public readonly struct FileEntry
{
    /// <summary>创建条目。</summary>
    public FileEntry(string name, uint attributes, long size, DateTimeOffset lastWriteUtc)
    {
        Name = name;
        Attributes = attributes;
        Size = size;
        LastWriteUtc = lastWriteUtc;
    }

    /// <summary>条目名称（不含目录）。</summary>
    public string Name { get; }

    /// <summary>原始 <c>dwFileAttributes</c> 位标志。</summary>
    public uint Attributes { get; }

    /// <summary>文件大小（字节）；目录为 0。</summary>
    public long Size { get; }

    /// <summary>最后写入时间（UTC）。</summary>
    public DateTimeOffset LastWriteUtc { get; }

    /// <summary>是否为目录。</summary>
    public bool IsDirectory => (Attributes & NativeMethods.FileAttributeDirectory) != 0;

    /// <summary>是否为重解析点（符号链接 / junction / 云占位符）。</summary>
    /// <remarks>
    /// 区分这一位很重要：遍历时若不识别重解析点，遇到目录符号链接会"穿透"进去，
    /// 造成重复枚举甚至无限递归（PRD §5.4）。
    /// </remarks>
    public bool IsReparsePoint => (Attributes & NativeMethods.FileAttributeReparsePoint) != 0;
}
