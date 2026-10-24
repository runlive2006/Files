namespace Files.Core.Services.Settings;

/// <summary>设置项的数据类型。</summary>
public enum SettingKind
{
    /// <summary>布尔开关。</summary>
    Boolean,

    /// <summary>整数。</summary>
    Integer,

    /// <summary>枚举（下拉）。</summary>
    Enum,

    /// <summary>字符串。</summary>
    Text,

    /// <summary>目录路径。</summary>
    DirectoryPath,
}

/// <summary>
/// 单个设置项的元数据。
/// </summary>
/// <remarks>
/// 界面由这份元数据自动生成，而不是为 34 项各写一段 XAML。
/// 同时它也是导入导出、命令行覆盖、以及"不可关闭项"约束的单一事实来源。
/// </remarks>
public sealed class SettingDescriptor
{
    /// <summary>完整键名，例如 <c>View.ShowHidden</c>。</summary>
    public required string Key { get; init; }

    /// <summary>分组名（对应设置界面左侧导航）。</summary>
    public required string Group { get; init; }

    /// <summary>显示名称。</summary>
    public required string DisplayName { get; init; }

    /// <summary>说明文字（也会被搜索匹配）。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>数据类型。</summary>
    public required SettingKind Kind { get; init; }

    /// <summary>整数的最小值。</summary>
    public int Min { get; init; } = int.MinValue;

    /// <summary>整数的最大值。</summary>
    public int Max { get; init; } = int.MaxValue;

    /// <summary>整数的步进。</summary>
    public int Step { get; init; } = 1;

    /// <summary>枚举可选值（值 → 显示文字）。</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Options { get; init; } = [];

    /// <summary>
    /// 是否只读。
    /// </summary>
    /// <remarks>
    /// 用于"永久删除确认"这类安全项：显示在界面上让用户知道它的存在，
    /// 但不允许修改（SETTINGS §5.4）。
    /// </remarks>
    public bool IsReadOnly { get; init; }

    /// <summary>只读时的说明。</summary>
    public string? ReadOnlyReason { get; init; }

    /// <summary>
    /// 修改该项是否需要重新枚举目录。
    /// </summary>
    /// <remarks>
    /// 对应设计文档的 C 档（SETTINGS §5.6）：
    /// 隐藏属性只有枚举时才能拿到，因此「显示隐藏文件」必须重读目录，
    /// 而「显示扩展名」只需改显示文本。
    /// </remarks>
    public bool RequiresReenumerate { get; init; }
}

/// <summary>
/// 全部设置项的元数据目录。
/// </summary>
public static class SettingCatalog
{
    /// <summary>分组显示顺序。</summary>
    public static readonly IReadOnlyList<string> GroupOrder =
        ["常规", "视图", "文件操作", "标签与栏", "性能", "高级"];

    /// <summary>全部设置项。</summary>
    public static readonly IReadOnlyList<SettingDescriptor> All =
    [
        // ---------- 常规 ----------
        new()
        {
            Key = "General.StartupMode",
            Group = "常规",
            DisplayName = "启动时打开",
            Description = "决定程序启动后显示什么位置。",
            Kind = SettingKind.Enum,
            Options =
            [
                new("LastSession", "恢复上次会话"),
                new("Home", "用户目录"),
                new("FixedPath", "指定位置"),
            ],
        },
        new()
        {
            Key = "General.FixedPath",
            Group = "常规",
            DisplayName = "指定位置",
            Description = "启动时打开指定位置时使用。",
            Kind = SettingKind.DirectoryPath,
        },
        new()
        {
            Key = "General.RestoreTabs",
            Group = "常规",
            DisplayName = "恢复各栏标签",
            Description = "关闭时只恢复每栏当前标签。",
            Kind = SettingKind.Boolean,
        },
        new()
        {
            Key = "General.SingleInstance",
            Group = "常规",
            DisplayName = "单实例运行",
            Description = "再次启动时把路径转交给已打开的窗口，而不是新开进程。",
            Kind = SettingKind.Boolean,
        },

        // ---------- 视图 ----------
        new()
        {
            Key = "View.DefaultMode",
            Group = "视图",
            DisplayName = "默认视图",
            Description = "新标签使用的视图模式。",
            Kind = SettingKind.Enum,
            Options =
            [
                new("Details", "详情"),
                new("List", "列表"),
                new("Thumbnails", "缩略图"),
            ],
        },
        new()
        {
            Key = "View.IconSize",
            Group = "视图",
            DisplayName = "图标大小",
            Description = "详情与列表视图中的图标尺寸（像素）。",
            Kind = SettingKind.Integer,
            Min = 16,
            Max = 32,
            Step = 4,
        },
        new()
        {
            Key = "View.ThumbnailSize",
            Group = "视图",
            DisplayName = "缩略图大小",
            Description = "缩略图视图的单元格边长（像素）。",
            Kind = SettingKind.Integer,
            Min = 96,
            Max = 256,
            Step = 16,
        },
        new()
        {
            Key = "View.ShowHidden",
            Group = "视图",
            DisplayName = "显示隐藏文件",
            Description = "显示隐藏文件与系统文件。需要重新读取目录。",
            Kind = SettingKind.Boolean,
            RequiresReenumerate = true,
        },
        new()
        {
            Key = "View.ShowExtensions",
            Group = "视图",
            DisplayName = "显示扩展名",
            Description = "显示文件扩展名。默认开启，便于识别真实文件类型。",
            Kind = SettingKind.Boolean,
        },
        new()
        {
            Key = "View.ShowStatusBar",
            Group = "视图",
            DisplayName = "显示状态栏",
            Kind = SettingKind.Boolean,
        },
        new()
        {
            Key = "View.ShowDetailsPane",
            Group = "视图",
            DisplayName = "显示详情窗格",
            Description = "在窗口底部显示选中项的信息。",
            Kind = SettingKind.Boolean,
        },
        new()
        {
            Key = "View.RowHeight",
            Group = "视图",
            DisplayName = "行高",
            Kind = SettingKind.Enum,
            Options =
            [
                new("Compact", "紧凑"),
                new("Normal", "标准"),
                new("Comfortable", "宽松"),
            ],
        },
        new()
        {
            Key = "View.AlternateRowColor",
            Group = "视图",
            DisplayName = "交替行背景色",
            Kind = SettingKind.Boolean,
        },
        new()
        {
            Key = "View.FolderFirst",
            Group = "视图",
            DisplayName = "文件夹优先",
            Description = "关闭后文件夹与文件按排序键混合排列。",
            Kind = SettingKind.Boolean,
        },
        new()
        {
            Key = "View.ShowGridLines",
            Group = "视图",
            DisplayName = "显示网格线",
            Kind = SettingKind.Boolean,
        },
        new()
        {
            Key = "View.InheritFolderView",
            Group = "视图",
            DisplayName = "记住每个文件夹的视图",
            Description = "关闭后所有文件夹统一使用默认视图。",
            Kind = SettingKind.Boolean,
        },

        // ---------- 文件操作 ----------
        new()
        {
            Key = "FileOps.ConfirmDelete",
            Group = "文件操作",
            DisplayName = "删除前确认",
            Description = "删除到回收站前弹出确认。",
            Kind = SettingKind.Boolean,
        },
        new()
        {
            Key = "FileOps.ConfirmPermanentDelete",
            Group = "文件操作",
            DisplayName = "永久删除前确认",
            Kind = SettingKind.Boolean,
            IsReadOnly = true,
            ReadOnlyReason = "不可撤销的数据丢失，不提供关闭选项。",
        },
        new()
        {
            Key = "FileOps.DeleteToRecycleBin",
            Group = "文件操作",
            DisplayName = "删除到回收站",
            Description = "关闭后 Del 键直接永久删除。",
            Kind = SettingKind.Boolean,
        },
        new()
        {
            Key = "FileOps.ConfirmDragMove",
            Group = "文件操作",
            DisplayName = "拖放移动前确认",
            Kind = SettingKind.Boolean,
        },
        new()
        {
            Key = "FileOps.OpenWithSingleClick",
            Group = "文件操作",
            DisplayName = "单击打开",
            Description = "类似网页的单击打开方式。",
            Kind = SettingKind.Boolean,
        },
        new()
        {
            Key = "FileOps.DoubleClickOnFolder",
            Group = "文件操作",
            DisplayName = "双击文件夹",
            Kind = SettingKind.Enum,
            Options =
            [
                new("Open", "在当前标签进入"),
                new("OpenInNewTab", "在新标签打开"),
            ],
        },

        // ---------- 标签与栏 ----------
        new()
        {
            Key = "Panes.ShowTabStripAlways",
            Group = "标签与栏",
            DisplayName = "始终显示标签条",
            Description = "关闭时仅在标签数 ≥2 时显示。",
            Kind = SettingKind.Boolean,
        },
        new()
        {
            Key = "Panes.NewTabInheritsPath",
            Group = "标签与栏",
            DisplayName = "新标签继承当前路径",
            Kind = SettingKind.Boolean,
        },
        new()
        {
            Key = "Panes.CloseWindowWithLastTab",
            Group = "标签与栏",
            DisplayName = "关闭最后标签时关闭窗口",
            Kind = SettingKind.Boolean,
        },
        new()
        {
            Key = "Panes.MaxClosedTabHistory",
            Group = "标签与栏",
            DisplayName = "可恢复标签数",
            Description = "Ctrl+Shift+T 可恢复的已关闭标签数量。",
            Kind = SettingKind.Integer,
            Min = 0,
            Max = 50,
        },

        // ---------- 性能 ----------
        new()
        {
            Key = "Perf.IconCacheSize",
            Group = "性能",
            DisplayName = "图标缓存条目",
            Kind = SettingKind.Integer,
            Min = 128,
            Max = 4096,
            Step = 128,
        },
        new()
        {
            Key = "Perf.EnableThumbnailDecoding",
            Group = "性能",
            DisplayName = "解码真实缩略图",
            Description = "对图片生成真实缩略图。尚未实现。",
            Kind = SettingKind.Boolean,
            IsReadOnly = true,
            ReadOnlyReason = "该功能尚未实现。",
        },
        new()
        {
            Key = "Perf.ShowPerfHud",
            Group = "性能",
            DisplayName = "显示性能 HUD",
            Description = "状态栏显示 FPS、最差帧、内存。",
            Kind = SettingKind.Boolean,
        },

        // ---------- 高级 ----------
        new()
        {
            Key = "Advanced.LogLevel",
            Group = "高级",
            DisplayName = "日志级别",
            Kind = SettingKind.Enum,
            Options =
            [
                new("Error", "错误"),
                new("Warning", "警告"),
                new("Info", "信息"),
                new("Debug", "调试"),
            ],
        },
        new()
        {
            Key = "Advanced.KeepCrashLogs",
            Group = "高级",
            DisplayName = "保留崩溃日志份数",
            Kind = SettingKind.Integer,
            Min = 0,
            Max = 100,
        },
        new()
        {
            Key = "Advanced.EnableShellExtensions",
            Group = "高级",
            DisplayName = "加载第三方右键扩展",
            Description = "实验性功能，需要进程隔离。",
            Kind = SettingKind.Boolean,
        },
        new()
        {
            Key = "Advanced.ConfigDirectory",
            Group = "高级",
            DisplayName = "配置目录",
            Description = "留空则使用程序目录下的 data。",
            Kind = SettingKind.DirectoryPath,
        },
    ];

    /// <summary>按分组取设置项。</summary>
    public static IReadOnlyList<SettingDescriptor> ForGroup(string group) =>
        [.. All.Where(d => string.Equals(d.Group, group, StringComparison.Ordinal))];

    /// <summary>按名称或说明搜索设置项。</summary>
    public static IReadOnlyList<SettingDescriptor> Search(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return All;
        }

        return
        [
            .. All.Where(d =>
                d.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || d.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || d.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase)),
        ];
    }

    /// <summary>按键名查找。</summary>
    public static SettingDescriptor? Find(string key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));
}
