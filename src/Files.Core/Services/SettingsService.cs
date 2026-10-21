using System.Text.Json;
using System.Text.Json.Serialization;

namespace Files.Core.Services;

/// <summary>
/// 应用设置。字段刻意保持扁平且可序列化，便于导入导出与版本迁移（PRD §5.5）。
/// </summary>
public sealed class AppSettings
{
    /// <summary>当前 schema 版本；升级时递增并在 <see cref="SettingsService"/> 中迁移。</summary>
    public int SchemaVersion { get; set; } = SettingsService.CurrentSchemaVersion;

    /// <summary>窗口几何。</summary>
    public WindowSettings Window { get; set; } = new();

    /// <summary>列宽（列名 → 像素）。</summary>
    public Dictionary<string, double> ColumnWidths { get; set; } = [];

    /// <summary>排序列。</summary>
    public string SortColumn { get; set; } = "Name";

    /// <summary>是否降序。</summary>
    public bool SortDescending { get; set; }

    /// <summary>视图模式。</summary>
    public string ViewMode { get; set; } = "Details";

    /// <summary>上次打开的目录。</summary>
    public string LastPath { get; set; } = string.Empty;
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

/// <summary>
/// 设置读写：原子写入、损坏自愈、版本迁移。
/// </summary>
/// <remarks>
/// 设计要点（PRD §5.5）：
/// <list type="bullet">
/// <item>写入走"临时文件 + 替换"，断电不会留下半个 JSON；</item>
/// <item>解析失败不阻塞启动，把损坏文件另存为 <c>.corrupt</c> 后回落到默认值；</item>
/// <item>schema 版本不匹配时先迁移再使用。</item>
/// </list>
/// </remarks>
public sealed class SettingsService
{
    /// <summary>当前 schema 版本。</summary>
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = true,
    };

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

    /// <summary>
    /// 读取设置。文件不存在或损坏时回落到默认值，不抛异常。
    /// </summary>
    public void Load()
    {
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
            LoadedFromDisk = true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // 保留现场供排查，然后以默认值继续启动
            TryPreserveCorruptFile();
            Current = new AppSettings();
            LoadedFromDisk = false;
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
        string json = JsonSerializer.Serialize(Current, JsonOptions);

        string tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);

        // 同一卷内的 Move(overwrite) 是原子替换；比 WriteAllText 直接覆盖安全得多
        File.Move(tempPath, FilePath, overwrite: true);
    }

    private AppSettings Migrate(AppSettings settings)
    {
        // v1 是初始版本，尚无迁移步骤。后续版本在此逐级升级。
        if (settings.SchemaVersion < CurrentSchemaVersion)
        {
            settings.SchemaVersion = CurrentSchemaVersion;
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
