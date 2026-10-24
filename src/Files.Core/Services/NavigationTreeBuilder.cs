namespace Files.Core.Services;

/// <summary>
/// 导航树的节点种类。
/// </summary>
public enum NavigationNodeKind
{
    /// <summary>分组标题（不可导航，仅用于分隔）。</summary>
    Group,

    /// <summary>固定位置（桌面、下载等已知文件夹）。</summary>
    KnownFolder,

    /// <summary>驱动器（盘符或网络位置）。</summary>
    Drive,

    /// <summary>普通文件夹（懒加载）。</summary>
    Folder,
}

/// <summary>
/// 导航树的一个节点。
/// </summary>
/// <remarks>
/// 设计要点（NAVIGATION-AND-PANES §2.2）：
/// <list type="bullet">
/// <item>子节点<b>懒加载</b>：未展开的节点不枚举子项，避免启动时把整棵树读出来；</item>
/// <item>节点自身只持有路径与显示信息，不持有子项集合 —— 子项由界面层按需请求。</item>
/// </list>
/// </remarks>
public sealed class NavigationNode
{
    /// <summary>创建节点。</summary>
    public NavigationNode(
        NavigationNodeKind kind,
        string displayName,
        string? path,
        bool hasChildren)
    {
        Kind = kind;
        DisplayName = displayName;
        Path = path;
        HasChildren = hasChildren;
    }

    /// <summary>节点种类。</summary>
    public NavigationNodeKind Kind { get; }

    /// <summary>显示名称。</summary>
    public string DisplayName { get; }

    /// <summary>对应路径；分组类节点为 <see langword="null"/>。</summary>
    public string? Path { get; }

    /// <summary>是否可能有子节点（决定是否显示展开箭头）。</summary>
    /// <remarks>
    /// 这是一个"快速判断"，不做磁盘访问：对目录一律认为可能有子项。
    /// 真正的子项在展开时才枚举。
    /// </remarks>
    public bool HasChildren { get; }

    /// <summary>是否可导航（分组节点不可）。</summary>
    public bool IsNavigable => Kind != NavigationNodeKind.Group && !string.IsNullOrEmpty(Path);
}

/// <summary>
/// 构建导航树的根节点集合。
/// </summary>
/// <remarks>
/// 只负责生成<b>第一层</b>与已知文件夹层，不递归。
/// 这样启动时不需要遍历磁盘，也不会因为网络盘无响应而卡住。
/// </remarks>
public sealed class NavigationTreeBuilder
{
    /// <summary>
    /// 构建根级节点。
    /// </summary>
    /// <param name="includeDrives">是否包含驱动器节点（此电脑）。</param>
    public IReadOnlyList<NavigationNode> BuildRoot(bool includeDrives = true)
    {
        var nodes = new List<NavigationNode>
        {
            new(NavigationNodeKind.Group, "常用位置", null, false),
        };

        // 已知文件夹：只列出确实存在的，避免出现点了没反应的死节点
        AddKnownFolder(nodes, Environment.SpecialFolder.Desktop, "桌面");
        AddKnownFolder(nodes, Environment.SpecialFolder.UserProfile, "用户目录");
        AddKnownFolder(nodes, Environment.SpecialFolder.MyDocuments, "文档");
        AddKnownFolder(nodes, Environment.SpecialFolder.MyPictures, "图片");
        AddKnownFolder(nodes, Environment.SpecialFolder.MyMusic, "音乐");
        AddKnownFolder(nodes, Environment.SpecialFolder.MyVideos, "视频");

        if (includeDrives)
        {
            nodes.Add(new NavigationNode(NavigationNodeKind.Group, "此电脑", null, false));

            foreach (NavigationNode drive in BuildDrives())
            {
                nodes.Add(drive);
            }
        }

        return nodes;
    }

    /// <summary>
    /// 枚举驱动器节点。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="DriveInfo.GetDrives"/> 而不是 <c>GetLogicalDrives</c>：
    /// 前者能直接拿到卷标与类型，且对未就绪的驱动器更宽容。
    /// 对每个驱动器都做异常保护 —— 网络盘在断连时抛异常是常态。
    /// </remarks>
    public IReadOnlyList<NavigationNode> BuildDrives()
    {
        var result = new List<NavigationNode>();

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return result;
        }

        foreach (DriveInfo drive in drives)
        {
            try
            {
                // 未就绪的光驱/读卡器不应出现在树里
                if (!drive.IsReady)
                {
                    continue;
                }

                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.Name.TrimEnd('\\')
                    : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";

                result.Add(new NavigationNode(
                    NavigationNodeKind.Drive,
                    label,
                    drive.RootDirectory.FullName,
                    hasChildren: true));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 单个驱动器读取失败不影响其他驱动器
            }
        }

        return result;
    }

    /// <summary>
    /// 枚举某个目录的子文件夹（用于展开时懒加载）。
    /// </summary>
    /// <remarks>
    /// 只返回文件夹，不返回文件 —— 与资源管理器的导航树一致。
    /// 无权限的目录返回空列表而不是抛异常，避免展开时打断用户。
    /// </remarks>
    public IReadOnlyList<NavigationNode> GetChildFolders(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return [];
        }

        var result = new List<NavigationNode>();

        try
        {
            var directory = new DirectoryInfo(directoryPath);
            if (!directory.Exists)
            {
                return result;
            }

            foreach (DirectoryInfo child in directory.EnumerateDirectories())
            {
                // 跳过隐藏与系统目录：树里显示它们会非常嘈杂，且多数不可访问
                if ((child.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                {
                    continue;
                }

                result.Add(new NavigationNode(
                    NavigationNodeKind.Folder,
                    child.Name,
                    child.FullName,
                    hasChildren: true));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                       or IOException
                                       or ArgumentException
                                       or PathTooLongException)
        {
            return result;
        }

        result.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, b.DisplayName));
        return result;
    }

    private static void AddKnownFolder(List<NavigationNode> nodes, Environment.SpecialFolder folder, string label)
    {
        try
        {
            string path = Environment.GetFolderPath(folder);

            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            nodes.Add(new NavigationNode(NavigationNodeKind.KnownFolder, label, path, hasChildren: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 某个已知文件夹不可用时跳过，不阻塞整棵树
        }
    }
}
