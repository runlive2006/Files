using Files.Core.Services;
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
        Assert.AreEqual("Name", service.Current.SortColumn);
    }

    [TestMethod]
    public void SaveThenLoad_RoundTripsValues()
    {
        var writer = new SettingsService(_filePath);
        writer.Load();
        writer.Current.LastPath = @"D:\Work";
        writer.Current.SortColumn = "Size";
        writer.Current.SortDescending = true;
        writer.Current.ViewMode = "List";
        writer.Current.ColumnWidths["Name"] = 420;
        writer.Save();

        var reader = new SettingsService(_filePath);
        reader.Load();

        Assert.IsTrue(reader.LoadedFromDisk);
        Assert.AreEqual(@"D:\Work", reader.Current.LastPath);
        Assert.AreEqual("Size", reader.Current.SortColumn);
        Assert.IsTrue(reader.Current.SortDescending);
        Assert.AreEqual("List", reader.Current.ViewMode);
        Assert.AreEqual(420, reader.Current.ColumnWidths["Name"]);
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
        service.Current.LastPath = @"C:\First";
        service.Save();

        service.Current.LastPath = @"C:\Second";
        service.Save();

        var reader = new SettingsService(_filePath);
        reader.Load();

        Assert.AreEqual(@"C:\Second", reader.Current.LastPath);
    }

    [TestMethod]
    public void Load_CorruptFile_FallsBackToDefaultsAndPreservesBackup()
    {
        File.WriteAllText(_filePath, "{ 这不是合法 JSON ");

        var service = new SettingsService(_filePath);
        service.Load();

        Assert.IsFalse(service.LoadedFromDisk);
        Assert.AreEqual("Name", service.Current.SortColumn);
        Assert.IsTrue(File.Exists(_filePath + ".corrupt"), "损坏文件应被保留以便排查。");
    }

    [TestMethod]
    public void Load_OlderSchema_MigratesToCurrentVersion()
    {
        File.WriteAllText(_filePath, """{ "SchemaVersion": 0, "SortColumn": "Kind" }""");

        var service = new SettingsService(_filePath);
        service.Load();

        Assert.IsTrue(service.LoadedFromDisk);
        Assert.AreEqual(SettingsService.CurrentSchemaVersion, service.Current.SchemaVersion);
        Assert.AreEqual("Kind", service.Current.SortColumn);
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
