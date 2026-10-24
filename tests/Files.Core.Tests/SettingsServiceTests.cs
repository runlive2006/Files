using Files.Core.Models;
using Files.Core.Services;
using Files.Core.Services.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class SettingsServiceTests
{
    private string _directory = string.Empty;
    private string _filePath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "FilesSettings_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _filePath = Path.Combine(_directory, "settings.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var service = new SettingsService(_filePath);

        service.Load();

        Assert.IsFalse(service.LoadedFromDisk);
        Assert.AreEqual(SettingsService.CurrentSchemaVersion, service.Current.SchemaVersion);
        Assert.AreEqual(StartupMode.LastSession, service.Current.General.StartupMode);
        Assert.AreEqual(ViewMode.Details, service.Current.View.DefaultMode);
        Assert.IsTrue(service.Current.View.ShowExtensions, "扩展名默认显示（安全考虑）。");
        Assert.IsTrue(service.Current.FileOperations.ConfirmPermanentDelete, "永久删除确认默认开启。");
    }

    [TestMethod]
    public void SaveThenLoad_RoundTripsValues()
    {
        var writer = new SettingsService(_filePath);
        writer.Load();
        writer.Current.General.StartupMode = StartupMode.FixedPath;
        writer.Current.General.FixedPath = @"D:\Work";
        writer.Current.View.ShowHidden = true;
        writer.Current.View.ThumbnailSize = 192;
        writer.Current.FileOperations.DeleteToRecycleBin = false;
        writer.Current.Performance.IconCacheSize = 1024;
        writer.Save();

        var reader = new SettingsService(_filePath);
        reader.Load();

        Assert.IsTrue(reader.LoadedFromDisk);
        Assert.AreEqual(StartupMode.FixedPath, reader.Current.General.StartupMode);
        Assert.AreEqual(@"D:\Work", reader.Current.General.FixedPath);
        Assert.IsTrue(reader.Current.View.ShowHidden);
        Assert.AreEqual(192, reader.Current.View.ThumbnailSize);
        Assert.IsFalse(reader.Current.FileOperations.DeleteToRecycleBin);
        Assert.AreEqual(1024, reader.Current.Performance.IconCacheSize);
    }

    [TestMethod]
    public void Save_LeavesNoTemporaryFile()
    {
        var service = new SettingsService(_filePath);
        service.Load();
        service.Save();

        Assert.IsTrue(File.Exists(_filePath));
        Assert.IsFalse(File.Exists(_filePath + ".tmp"), "原子写入不应留下临时文件。");
    }

    [TestMethod]
    public void Save_OverwritesExistingFile()
    {
        var service = new SettingsService(_filePath);
        service.Load();
        service.Current.General.FixedPath = @"C:\First";
        service.Save();

        service.Current.General.FixedPath = @"C:\Second";
        service.Save();

        var reader = new SettingsService(_filePath);
        reader.Load();

        Assert.AreEqual(@"C:\Second", reader.Current.General.FixedPath);
    }

    [TestMethod]
    public void Load_CorruptFile_FallsBackToDefaultsAndPreservesBackup()
    {
        File.WriteAllText(_filePath, "{ 这不是合法 JSON ");

        var service = new SettingsService(_filePath);
        service.Load();

        Assert.IsFalse(service.LoadedFromDisk);
        Assert.AreEqual(ViewMode.Details, service.Current.View.DefaultMode);
        Assert.IsTrue(File.Exists(_filePath + ".corrupt"), "损坏文件应被保留以便排查。");
        Assert.IsNotEmpty(service.Warnings);
    }

    [TestMethod]
    public void Load_V1File_MigratesToV2()
    {
        // v1 的顶层字段结构
        File.WriteAllText(_filePath, """
            {
              "SchemaVersion": 1,
              "ViewMode": "List",
              "UseSystemIcons": false,
              "LastPath": "D:\\Legacy",
              "Panes": [],
              "SplitRatios": []
            }
            """);

        var service = new SettingsService(_filePath);
        service.Load();

        Assert.IsTrue(service.LoadedFromDisk);
        Assert.AreEqual(2, service.Current.SchemaVersion, "应升级到 v2。");
        Assert.AreEqual(ViewMode.List, service.Current.View.DefaultMode, "v1 的 ViewMode 应映射到 DefaultMode。");
        Assert.HasCount(1, service.Current.Panes, "v1 的 LastPath 应在 Panes 为空时兜底为单栏。");
        Assert.AreEqual(@"D:\Legacy", service.Current.Panes[0].Path);
    }

    [TestMethod]
    public void Load_OutOfRangeValues_AreClamped()
    {
        File.WriteAllText(_filePath, """
            {
              "SchemaVersion": 2,
              "View": { "IconSize": 999, "ThumbnailSize": 5 },
              "Performance": { "IconCacheSize": 999999 },
              "PaneBehavior": { "MaxClosedTabHistory": -3 }
            }
            """);

        var service = new SettingsService(_filePath);
        service.Load();

        Assert.AreEqual(32, service.Current.View.IconSize, "IconSize 应夹紧到上限 32。");
        Assert.AreEqual(96, service.Current.View.ThumbnailSize, "ThumbnailSize 应夹紧到下限 96。");
        Assert.AreEqual(4096, service.Current.Performance.IconCacheSize);
        Assert.AreEqual(0, service.Current.PaneBehavior.MaxClosedTabHistory);
        Assert.IsNotEmpty(service.Warnings, "夹紧操作应产生警告。");
    }

    [TestMethod]
    public void Load_PermanentDeleteConfirmationForcedTrue()
    {
        File.WriteAllText(_filePath, """
            {
              "SchemaVersion": 2,
              "FileOperations": { "ConfirmPermanentDelete": false }
            }
            """);

        var service = new SettingsService(_filePath);
        service.Load();

        Assert.IsTrue(
            service.Current.FileOperations.ConfirmPermanentDelete,
            "安全项不可通过配置文件关闭。");
    }

    [TestMethod]
    public void ReadValue_ReturnsCurrentValueForEveryCatalogEntry()
    {
        var service = new SettingsService(_filePath);
        service.Load();

        foreach (SettingDescriptor descriptor in SettingCatalog.All)
        {
            string value = service.ReadValue(descriptor);
            Assert.IsNotNull(value, $"设置项 {descriptor.Key} 应能读出值。");
        }
    }

    [TestMethod]
    public void WriteValue_RoundTripsBooleanAndEnum()
    {
        var service = new SettingsService(_filePath);
        service.Load();

        SettingDescriptor hidden = SettingCatalog.Find("View.ShowHidden")!;
        Assert.IsTrue(service.WriteValue(hidden, "true"));
        Assert.IsTrue(service.Current.View.ShowHidden);

        SettingDescriptor density = SettingCatalog.Find("View.RowHeight")!;
        Assert.IsTrue(service.WriteValue(density, "Compact"));
        Assert.AreEqual(RowDensity.Compact, service.Current.View.RowHeight);
    }

    [TestMethod]
    public void WriteValue_RejectsReadOnlyEntries()
    {
        var service = new SettingsService(_filePath);
        service.Load();

        SettingDescriptor permanentDelete = SettingCatalog.Find("FileOps.ConfirmPermanentDelete")!;

        Assert.IsTrue(permanentDelete.IsReadOnly);
        Assert.IsFalse(service.WriteValue(permanentDelete, "false"), "只读项必须拒绝写入。");
        Assert.IsTrue(service.Current.FileOperations.ConfirmPermanentDelete);
    }

    [TestMethod]
    public void WriteValue_ClampsIntegerToCatalogRange()
    {
        var service = new SettingsService(_filePath);
        service.Load();

        SettingDescriptor iconSize = SettingCatalog.Find("View.IconSize")!;
        service.WriteValue(iconSize, "9999");

        Assert.AreEqual(32, service.Current.View.IconSize);
    }

    [TestMethod]
    public void WriteValue_NotifiesChanged()
    {
        var service = new SettingsService(_filePath);
        service.Load();

        string? reported = null;
        service.Changed += (_, group) => reported = group;

        service.WriteValue(SettingCatalog.Find("View.ShowHidden")!, "true");

        Assert.AreEqual("视图", reported, "应通知受影响的分组。");
    }

    [TestMethod]
    public void Save_CreatesMissingDirectory()
    {
        string nested = Path.Combine(_directory, "a", "b", "settings.json");
        var service = new SettingsService(nested);
        service.Load();
        service.Save();

        Assert.IsTrue(File.Exists(nested));
    }

    [TestMethod]
    public void Constructor_RejectsBlankPath()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SettingsService("  "));
    }
}
