using System.Text.Json;
using System.Text.Json.Serialization;
using Files.Core.Models;
using Files.Core.Services.Settings;

namespace Files.Core.Services;

/// <summary>
/// 设置读写：原子写入、损坏自愈、版本迁移、越界值夹紧。
/// </summary>
/// <remarks>
/// 设计要点（PRD §5.5）：
/// <list type="bullet">
/// <item>写入走"临时文件 + 替换"，断电不会留下半个 JSON；</item>
/// <item>解析失败不阻塞启动，把损坏文件另存为 <c>.corrupt</c> 后回落到默认值；</item>
/// <item>schema 版本不匹配时先迁移再使用；</item>
/// <item>手工编辑产生的越界值被夹紧到合法范围并记日志，而不是让程序行为异常。</item>
/// </list>
/// </remarks>
public sealed class SettingsService
{
    /// <summary>当前 schema 版本。</summary>
    public const int CurrentSchemaVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly List<string> _warnings = [];

    /// <summary>创建设置服务。</summary>
    /// <param name="filePath">设置文件路径。</param>
    public SettingsService(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
    }

    /// <summary>设置文件路径。</summary>
    public string FilePath { get; }

    /// <summary>当前设置（<see cref="Load"/> 之后可用）。</summary>
    public AppSettings Current { get; private set; } = new();

    /// <summary>上次 <see cref="Load"/> 是否读到了有效文件。</summary>
    public bool LoadedFromDisk { get; private set; }

    /// <summary>加载过程中产生的警告（越界值被夹紧、未知枚举被重置等）。</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>设置发生变化时触发，参数为受影响的键前缀（如 <c>View</c>）。</summary>
    public event EventHandler<string>? Changed;

    /// <summary>
    /// 读取设置。文件不存在或损坏时回落到默认值，不抛异常。
    /// </summary>
    public void Load()
    {
        _warnings.Clear();

        if (!File.Exists(FilePath))
        {
            Current = new AppSettings();
            LoadedFromDisk = false;
            return;
        }

        try
        {
            string json = File.ReadAllText(FilePath);
            AppSettings? parsed = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

            if (parsed is null)
            {
                throw new JsonException("反序列化结果为 null。");
            }

            Current = Migrate(parsed);
            Clamp(Current);
            LoadedFromDisk = true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            TryPreserveCorruptFile();
            Current = new AppSettings();
            LoadedFromDisk = false;
            _warnings.Add($"设置文件无法解析，已使用默认值：{ex.Message}");
        }
    }

    /// <summary>
    /// 原子写入设置。
    /// </summary>
    public void Save()
    {
        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Current.SchemaVersion = CurrentSchemaVersion;

        // 旧字段不写回，避免文件里同时存在新旧两套
        Current.LegacyViewMode = null;
        Current.LegacyUseSystemIcons = null;
        Current.LegacyLastPath = null;

        string json = JsonSerializer.Serialize(Current, JsonOptions);

        string tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);

        // 同一卷内的 Move(overwrite) 是原子替换；比直接覆盖安全得多
        File.Move(tempPath, FilePath, overwrite: true);
    }

    /// <summary>通知外部某项设置已变更。</summary>
    public void NotifyChanged(string keyPrefix) => Changed?.Invoke(this, keyPrefix);

    /// <summary>
    /// 读取单项设置的当前值（用于界面自动生成）。
    /// </summary>
    /// <returns>值的字符串表示；布尔用 <c>true</c>/<c>false</c>，枚举用名称。</returns>
    public string ReadValue(SettingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return descriptor.Key switch
        {
            "General.StartupMode" => Current.General.StartupMode.ToString(),
            "General.FixedPath" => Current.General.FixedPath,
            "General.RestoreTabs" => Bool(Current.General.RestoreTabs),
            "General.SingleInstance" => Bool(Current.General.SingleInstance),

            "View.DefaultMode" => Current.View.DefaultMode.ToString(),
            "View.IconSize" => Current.View.IconSize.ToString(),
            "View.ThumbnailSize" => Current.View.ThumbnailSize.ToString(),
            "View.ShowHidden" => Bool(Current.View.ShowHidden),
            "View.ShowExtensions" => Bool(Current.View.ShowExtensions),
            "View.ShowStatusBar" => Bool(Current.View.ShowStatusBar),
            "View.ShowDetailsPane" => Bool(Current.View.ShowDetailsPane),
            "View.ShowNavigationPane" => Bool(Current.View.ShowNavigationPane),
            "View.RowHeight" => Current.View.RowHeight.ToString(),
            "View.AlternateRowColor" => Bool(Current.View.AlternateRowColor),
            "View.FolderFirst" => Bool(Current.View.FolderFirst),
            "View.ShowGridLines" => Bool(Current.View.ShowGridLines),
            "View.InheritFolderView" => Bool(Current.View.InheritFolderView),

            "FileOps.ConfirmDelete" => Bool(Current.FileOperations.ConfirmDelete),
            "FileOps.ConfirmPermanentDelete" => Bool(true),
            "FileOps.DeleteToRecycleBin" => Bool(Current.FileOperations.DeleteToRecycleBin),
            "FileOps.ConfirmDragMove" => Bool(Current.FileOperations.ConfirmDragMove),
            "FileOps.OpenWithSingleClick" => Bool(Current.FileOperations.OpenWithSingleClick),
            "FileOps.DoubleClickOnFolder" => Current.FileOperations.DoubleClickOnFolder.ToString(),

            "Panes.ShowTabStripAlways" => Bool(Current.PaneBehavior.ShowTabStripAlways),
            "Panes.NewTabInheritsPath" => Bool(Current.PaneBehavior.NewTabInheritsPath),
            "Panes.CloseWindowWithLastTab" => Bool(Current.PaneBehavior.CloseWindowWithLastTab),
            "Panes.MaxClosedTabHistory" => Current.PaneBehavior.MaxClosedTabHistory.ToString(),

            "Perf.IconCacheSize" => Current.Performance.IconCacheSize.ToString(),
            "Perf.EnableThumbnailDecoding" => Bool(Current.Performance.EnableThumbnailDecoding),
            "Perf.ShowPerfHud" => Bool(Current.Performance.ShowPerfHud),

            "Advanced.LogLevel" => Current.Advanced.LogLevel.ToString(),
            "Advanced.KeepCrashLogs" => Current.Advanced.KeepCrashLogs.ToString(),
            "Advanced.EnableShellExtensions" => Bool(Current.Advanced.EnableShellExtensions),
            "Advanced.ConfigDirectory" => Current.Advanced.ConfigDirectory,

            _ => string.Empty,
        };
    }

    /// <summary>
    /// 写入单项设置。只读项与未知键会被拒绝。
    /// </summary>
    /// <returns>是否实际写入。</returns>
    public bool WriteValue(SettingDescriptor descriptor, string value)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(value);

        if (descriptor.IsReadOnly)
        {
            return false;
        }

        bool ok = true;

        switch (descriptor.Key)
        {
            case "General.StartupMode":
                ok = TryEnum(value, out StartupMode startup);
                if (ok) { Current.General.StartupMode = startup; }
                break;

            case "General.FixedPath":
                Current.General.FixedPath = value;
                break;

            case "General.RestoreTabs":
                Current.General.RestoreTabs = ParseBool(value);
                break;

            case "General.SingleInstance":
                Current.General.SingleInstance = ParseBool(value);
                break;

            case "View.DefaultMode":
                ok = TryEnum(value, out ViewMode defaultMode);
                if (ok) { Current.View.DefaultMode = defaultMode; }
                break;

            case "View.IconSize":
                Current.View.IconSize = ClampInt(value, descriptor, Current.View.IconSize);
                break;

            case "View.ThumbnailSize":
                Current.View.ThumbnailSize = ClampInt(value, descriptor, Current.View.ThumbnailSize);
                break;

            case "View.ShowHidden":
                Current.View.ShowHidden = ParseBool(value);
                break;

            case "View.ShowExtensions":
                Current.View.ShowExtensions = ParseBool(value);
                break;

            case "View.ShowStatusBar":
                Current.View.ShowStatusBar = ParseBool(value);
                break;

            case "View.ShowDetailsPane":
                Current.View.ShowDetailsPane = ParseBool(value);
                break;

            case "View.ShowNavigationPane":
                Current.View.ShowNavigationPane = ParseBool(value);
                break;

            case "View.RowHeight":
                ok = TryEnum(value, out RowDensity density);
                if (ok) { Current.View.RowHeight = density; }
                break;

            case "View.AlternateRowColor":
                Current.View.AlternateRowColor = ParseBool(value);
                break;

            case "View.FolderFirst":
                Current.View.FolderFirst = ParseBool(value);
                break;

            case "View.ShowGridLines":
                Current.View.ShowGridLines = ParseBool(value);
                break;

            case "View.InheritFolderView":
                Current.View.InheritFolderView = ParseBool(value);
                break;

            case "FileOps.ConfirmDelete":
                Current.FileOperations.ConfirmDelete = ParseBool(value);
                break;

            case "FileOps.DeleteToRecycleBin":
                Current.FileOperations.DeleteToRecycleBin = ParseBool(value);
                break;

            case "FileOps.ConfirmDragMove":
                Current.FileOperations.ConfirmDragMove = ParseBool(value);
                break;

            case "FileOps.OpenWithSingleClick":
                Current.FileOperations.OpenWithSingleClick = ParseBool(value);
                break;

            case "FileOps.DoubleClickOnFolder":
                ok = TryEnum(value, out FolderDoubleClickAction action);
                if (ok) { Current.FileOperations.DoubleClickOnFolder = action; }
                break;

            case "Panes.ShowTabStripAlways":
                Current.PaneBehavior.ShowTabStripAlways = ParseBool(value);
                break;

            case "Panes.NewTabInheritsPath":
                Current.PaneBehavior.NewTabInheritsPath = ParseBool(value);
                break;

            case "Panes.CloseWindowWithLastTab":
                Current.PaneBehavior.CloseWindowWithLastTab = ParseBool(value);
                break;

            case "Panes.MaxClosedTabHistory":
                Current.PaneBehavior.MaxClosedTabHistory = ClampInt(value, descriptor, Current.PaneBehavior.MaxClosedTabHistory);
                break;

            case "Perf.IconCacheSize":
                Current.Performance.IconCacheSize = ClampInt(value, descriptor, Current.Performance.IconCacheSize);
                break;

            case "Perf.ShowPerfHud":
                Current.Performance.ShowPerfHud = ParseBool(value);
                break;

            case "Advanced.LogLevel":
                ok = TryEnum(value, out LogLevel level);
                if (ok) { Current.Advanced.LogLevel = level; }
                break;

            case "Advanced.KeepCrashLogs":
                Current.Advanced.KeepCrashLogs = ClampInt(value, descriptor, Current.Advanced.KeepCrashLogs);
                break;

            case "Advanced.EnableShellExtensions":
                Current.Advanced.EnableShellExtensions = ParseBool(value);
                break;

            case "Advanced.ConfigDirectory":
                Current.Advanced.ConfigDirectory = value;
                break;

            default:
                ok = false;
                break;
        }

        if (ok)
        {
            NotifyChanged(descriptor.Group);
        }

        return ok;
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static bool ParseBool(string value) =>
        bool.TryParse(value, out bool result) && result;

    private static bool TryEnum<T>(string value, out T result) where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out result);

    private static int ClampInt(string value, SettingDescriptor descriptor, int fallback)
    {
        if (!int.TryParse(value, out int parsed))
        {
            return fallback;
        }

        return Math.Clamp(parsed, descriptor.Min, descriptor.Max);
    }

    /// <summary>
    /// 把越界/非法值夹紧到合法范围，避免手工编辑导致的异常行为。
    /// </summary>
    private void Clamp(AppSettings settings)
    {
        settings.View.IconSize = ClampIntValue(settings.View.IconSize, 16, 32, "View.IconSize");
        settings.View.ThumbnailSize = ClampIntValue(settings.View.ThumbnailSize, 96, 256, "View.ThumbnailSize");
        settings.PaneBehavior.MaxClosedTabHistory = ClampIntValue(
            settings.PaneBehavior.MaxClosedTabHistory, 0, 50, "Panes.MaxClosedTabHistory");
        settings.Performance.IconCacheSize = ClampIntValue(
            settings.Performance.IconCacheSize, 128, 4096, "Perf.IconCacheSize");
        settings.Advanced.KeepCrashLogs = ClampIntValue(
            settings.Advanced.KeepCrashLogs, 0, 100, "Advanced.KeepCrashLogs");

        // 安全项强制为 true：即使文件里被改成 false，也不接受
        if (!settings.FileOperations.ConfirmPermanentDelete)
        {
            settings.FileOperations.ConfirmPermanentDelete = true;
            _warnings.Add("FileOps.ConfirmPermanentDelete 被强制重置为 true（安全项不可关闭）。");
        }

        if (!Enum.IsDefined(settings.General.StartupMode))
        {
            settings.General.StartupMode = StartupMode.LastSession;
            _warnings.Add("General.StartupMode 值非法，已重置为 LastSession。");
        }

        if (!Enum.IsDefined(settings.View.DefaultMode))
        {
            settings.View.DefaultMode = ViewMode.Details;
            _warnings.Add("View.DefaultMode 值非法，已重置为 Details。");
        }
    }

    /// <summary>把越界整数夹紧到合法范围，返回夹紧后的值。</summary>
    private int ClampIntValue(int value, int min, int max, string key)
    {
        if (value >= min && value <= max)
        {
            return value;
        }

        int clamped = Math.Clamp(value, min, max);
        _warnings.Add($"{key} 值 {value} 超出 [{min}, {max}]，已夹紧为 {clamped}。");
        return clamped;
    }

    /// <summary>
    /// schema 迁移。v1 → v2：把顶层旧字段搬进分组。
    /// </summary>
    private AppSettings Migrate(AppSettings settings)
    {
        if (settings.SchemaVersion >= CurrentSchemaVersion)
        {
            return settings;
        }

        // v1 → v2
        if (settings.SchemaVersion < 2)
        {
            // v1 的顶层 ViewMode 对应新版的默认视图模式
            if (!string.IsNullOrWhiteSpace(settings.LegacyViewMode)
                && Enum.TryParse(settings.LegacyViewMode, ignoreCase: true, out ViewMode mode))
            {
                settings.View.DefaultMode = mode;
            }

            // v1 的 UseSystemIcons 在新版中不再单独存在（系统图标恒开，由图标提供者决定）
            // 保留读取以避免解析失败，但不映射到任何新字段。

            // v1 的 LastPath 已经由 Panes[0].Path 承载；仅在缺失时兜底
            if (settings.Panes.Count == 0 && !string.IsNullOrWhiteSpace(settings.LegacyLastPath))
            {
                settings.Panes.Add(new PaneSettings { Path = settings.LegacyLastPath });
            }

            settings.SchemaVersion = 2;
        }

        return settings;
    }

    private void TryPreserveCorruptFile()
    {
        try
        {
            string backup = FilePath + ".corrupt";
            File.Copy(FilePath, backup, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 备份失败不应影响启动
        }
    }
}