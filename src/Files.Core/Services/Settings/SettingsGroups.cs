using Files.Core.Models;

namespace Files.Core.Services.Settings;

/// <summary>启动行为。</summary>
public enum StartupMode
{
    /// <summary>恢复上次会话。</summary>
    LastSession,

    /// <summary>打开用户目录。</summary>
    Home,

    /// <summary>打开指定路径。</summary>
    FixedPath,
}

/// <summary>行高密度。</summary>
public enum RowDensity
{
    /// <summary>紧凑。</summary>
    Compact,

    /// <summary>标准。</summary>
    Normal,

    /// <summary>宽松。</summary>
    Comfortable,
}

/// <summary>双击文件夹的行为。</summary>
public enum FolderDoubleClickAction
{
    /// <summary>在当前标签进入。</summary>
    Open,

    /// <summary>在新标签打开。</summary>
    OpenInNewTab,
}

/// <summary>日志级别。</summary>
public enum LogLevel
{
    /// <summary>仅错误。</summary>
    Error,

    /// <summary>警告及以上。</summary>
    Warning,

    /// <summary>信息及以上。</summary>
    Info,

    /// <summary>调试。</summary>
    Debug,
}

/// <summary>常规设置。</summary>
public sealed class GeneralSettings
{
    /// <summary>启动行为。</summary>
    public StartupMode StartupMode { get; set; } = StartupMode.LastSession;

    /// <summary><see cref="StartupMode.FixedPath"/> 时使用的路径。</summary>
    public string FixedPath { get; set; } = string.Empty;

    /// <summary>是否恢复各栏标签。</summary>
    public bool RestoreTabs { get; set; } = true;

    /// <summary>是否单实例运行。</summary>
    public bool SingleInstance { get; set; } = true;
}

/// <summary>视图设置。</summary>
public sealed class ViewSettings
{
    /// <summary>新标签的默认视图模式。</summary>
    public ViewMode DefaultMode { get; set; } = ViewMode.Details;

    /// <summary>详情/列表视图的图标尺寸（像素）。</summary>
    public int IconSize { get; set; } = 16;

    /// <summary>缩略图单元格边长（像素）。</summary>
    public int ThumbnailSize { get; set; } = 128;

    /// <summary>是否显示隐藏文件与系统文件。</summary>
    public bool ShowHidden { get; set; }

    /// <summary>是否显示文件扩展名。</summary>
    public bool ShowExtensions { get; set; } = true;

    /// <summary>是否显示状态栏。</summary>
    public bool ShowStatusBar { get; set; } = true;

    /// <summary>是否显示详情窗格。</summary>
    public bool ShowDetailsPane { get; set; }

    /// <summary>是否显示导航窗格。</summary>
    public bool ShowNavigationPane { get; set; } = true;

    /// <summary>导航窗格宽度（像素）。</summary>
    public double NavigationPaneWidth { get; set; } = 220;

    /// <summary>行高密度。</summary>
    public RowDensity RowHeight { get; set; } = RowDensity.Normal;

    /// <summary>是否使用交替行背景色。</summary>
    public bool AlternateRowColor { get; set; }

    /// <summary>是否让文件夹排在文件之前。</summary>
    public bool FolderFirst { get; set; } = true;

    /// <summary>是否显示网格线。</summary>
    public bool ShowGridLines { get; set; }

    /// <summary>是否记住每个文件夹各自的视图模式。</summary>
    public bool InheritFolderView { get; set; } = true;
}

/// <summary>文件操作设置。</summary>
public sealed class FileOperationSettings
{
    /// <summary>删除到回收站前是否确认。</summary>
    public bool ConfirmDelete { get; set; } = true;

    /// <summary>
    /// 永久删除前是否确认。
    /// </summary>
    /// <remarks>
    /// 该项<b>强制为 true</b>，不提供关闭入口（设计文档 SETTINGS §5.4）。
    /// 保留字段是为了在配置文件里显式呈现"不可关闭"这一事实。
    /// </remarks>
    public bool ConfirmPermanentDelete { get; set; } = true;

    /// <summary><c>Del</c> 键是否删除到回收站。</summary>
    public bool DeleteToRecycleBin { get; set; } = true;

    /// <summary>跨卷/跨栏拖放移动前是否确认。</summary>
    public bool ConfirmDragMove { get; set; }

    /// <summary>是否单击打开（Web 式）。</summary>
    public bool OpenWithSingleClick { get; set; }

    /// <summary>双击文件夹的行为。</summary>
    public FolderDoubleClickAction DoubleClickOnFolder { get; set; } = FolderDoubleClickAction.Open;
}

/// <summary>标签与栏设置。</summary>
public sealed class PaneBehaviorSettings
{
    /// <summary>是否始终显示标签条。</summary>
    public bool ShowTabStripAlways { get; set; }

    /// <summary>新标签是否继承当前路径。</summary>
    public bool NewTabInheritsPath { get; set; } = true;

    /// <summary>关闭最后一个标签时是否关闭窗口。</summary>
    public bool CloseWindowWithLastTab { get; set; }

    /// <summary>可恢复的已关闭标签数量上限。</summary>
    public int MaxClosedTabHistory { get; set; } = 10;
}

/// <summary>性能设置。</summary>
public sealed class PerformanceSettings
{
    /// <summary>图标缓存条目上限。</summary>
    public int IconCacheSize { get; set; } = 512;

    /// <summary>是否解码真实图片缩略图（尚未实现）。</summary>
    public bool EnableThumbnailDecoding { get; set; }

    /// <summary>是否显示性能 HUD。</summary>
    public bool ShowPerfHud { get; set; }
}

/// <summary>高级设置。</summary>
public sealed class AdvancedSettings
{
    /// <summary>日志级别。</summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Warning;

    /// <summary>保留的崩溃日志份数。</summary>
    public int KeepCrashLogs { get; set; } = 10;

    /// <summary>是否加载第三方右键扩展。</summary>
    public bool EnableShellExtensions { get; set; }

    /// <summary>配置目录覆盖；空表示使用程序目录下的 data。</summary>
    public string ConfigDirectory { get; set; } = string.Empty;
}
