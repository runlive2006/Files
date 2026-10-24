using System.Text.Json.Serialization;
using Files.Core.Services.Settings;

namespace Files.Core.Services;

/// <summary>
/// 应用设置。按功能分组，字段扁平可序列化，便于导入导出与版本迁移（PRD §5.5）。
/// </summary>
public sealed class AppSettings
{
    /// <summary>当前 schema 版本；升级时递增并在 <see cref="SettingsService"/> 中迁移。</summary>
    public int SchemaVersion { get; set; } = SettingsService.CurrentSchemaVersion;

    /// <summary>常规。</summary>
    public GeneralSettings General { get; set; } = new();

    /// <summary>视图。</summary>
    public ViewSettings View { get; set; } = new();

    /// <summary>文件操作。</summary>
    public FileOperationSettings FileOperations { get; set; } = new();

    /// <summary>标签与栏的行为。</summary>
    public PaneBehaviorSettings PaneBehavior { get; set; } = new();

    /// <summary>性能。</summary>
    public PerformanceSettings Performance { get; set; } = new();

    /// <summary>高级。</summary>
    public AdvancedSettings Advanced { get; set; } = new();

    // ---------- 窗口与布局（结构不变） ----------

    /// <summary>窗口几何。</summary>
    public WindowSettings Window { get; set; } = new();

    /// <summary>列宽（列名 → 像素）。</summary>
    public Dictionary<string, double> ColumnWidths { get; set; } = [];

    /// <summary>布局模式：Single / HorizontalSplit / VerticalSplit / Quad。</summary>
    public string LayoutMode { get; set; } = "Single";

    /// <summary>各栏的路径与标签。</summary>
    public List<PaneSettings> Panes { get; set; } = [];

    /// <summary>各分裂节点的分隔比例。</summary>
    public List<double> SplitRatios { get; set; } = [];

    // ---------- v1 旧字段（仅供迁移读取，写入时清空） ----------

    /// <summary>v1 的顶层视图模式。</summary>
    [JsonPropertyName("ViewMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyViewMode { get; set; }

    /// <summary>v1 的顶层系统图标开关。</summary>
    [JsonPropertyName("UseSystemIcons")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyUseSystemIcons { get; set; }

    /// <summary>v1 的上次路径。</summary>
    [JsonPropertyName("LastPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyLastPath { get; set; }
}

/// <summary>窗口几何设置。</summary>
public sealed class WindowSettings
{
    /// <summary>宽度。</summary>
    public double Width { get; set; } = 960;

    /// <summary>高度。</summary>
    public double Height { get; set; } = 600;

    /// <summary>左边距；<see langword="null"/> 表示尚未记录。</summary>
    /// <remarks>
    /// 用可空类型而不是 <see cref="double.NaN"/> 作哨兵：NaN 无法被 JSON 序列化，
    /// 会在保存时直接抛异常。
    /// </remarks>
    public double? Left { get; set; }

    /// <summary>上边距；<see langword="null"/> 表示尚未记录。</summary>
    public double? Top { get; set; }

    /// <summary>是否最大化。</summary>
    public bool Maximized { get; set; }
}

/// <summary>单栏的持久化状态。</summary>
public sealed class PaneSettings
{
    /// <summary>该栏当前目录（活动标签，保留兼容字段）。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>视图模式。</summary>
    public string ViewMode { get; set; } = "Details";

    /// <summary>该栏的标签路径列表，按标签条顺序。</summary>
    public List<string> Tabs { get; set; } = [];

    /// <summary>活动标签下标。</summary>
    public int ActiveTabIndex { get; set; }
}
