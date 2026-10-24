namespace Files.Core.Models;

/// <summary>
/// 文件系统中的一个条目（文件或文件夹）。
/// </summary>
/// <remarks>
/// 刻意设计为 <see langword="readonly struct"/>：10 万项时 class 的对象头与
/// 逐项引用会额外吃掉十几 MB，而条目本身没有任何需要继承或引用的语义。
/// <para>
/// 这里**不保存** <c>FullPath</c>。目录内每个条目的完整路径都是
/// "目录 + 名称"，逐项存一遍在 10 万项时会多占约 24MB（见 PERF-BASELINE §3.2）。
/// 需要完整路径时由 <see cref="DirectoryListing.GetFullPath"/> 现拼。
/// </para>
/// </remarks>
public readonly struct FileSystemEntry
{
    /// <summary>创建条目。</summary>
    public FileSystemEntry(
        string name,
        bool isDirectory,
        long sizeBytes,
        DateTimeOffset lastModified,
        bool isHidden = false)
    {
        Name = name;
        IsDirectory = isDirectory;
        SizeBytes = sizeBytes;
        LastModified = lastModified;
        IsHidden = isHidden;
    }

    /// <summary>条目在所在目录中的显示名称。</summary>
    public string Name { get; }

    /// <summary>是否为文件夹。</summary>
    public bool IsDirectory { get; }

    /// <summary>文件大小（字节）；文件夹为 0。</summary>
    public long SizeBytes { get; }

    /// <summary>最后修改时间（UTC）。</summary>
    public DateTimeOffset LastModified { get; }

    /// <summary>
    /// 是否为隐藏项（含系统文件）。
    /// </summary>
    /// <remarks>
    /// 枚举阶段就记录该标记，这样"显示隐藏文件"的切换只需在内存中过滤，
    /// 而不必为了拿到属性而二次访问磁盘。
    /// 注意：这与设计文档 SETTINGS §5.6 的 C 档描述一致 ——
    /// 切换开关仍需重枚举一次，因为默认情况下隐藏项根本没被列出来。
    /// </remarks>
    public bool IsHidden { get; }

    /// <summary>扩展名（含点），文件夹为空字符串。</summary>
    /// <remarks>每次访问都会计算，不要放进排序比较器里逐次调用。</remarks>
    public string Extension => IsDirectory ? string.Empty : Path.GetExtension(Name);
}
