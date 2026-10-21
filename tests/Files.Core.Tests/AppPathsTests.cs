using Files.Core.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class AppPathsTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilesPaths_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public void AllPaths_AreUnderExecutableDirectory()
    {
        var paths = new AppPaths(_root);

        // 强制约束：配置/日志/临时文件都必须落在 exe 目录之下
        foreach (string path in new[]
                 {
                     paths.DataDirectory,
                     paths.SettingsFile,
                     paths.LogDirectory,
                     paths.TempDirectory,
                 })
        {
            Assert.StartsWith(paths.BaseDirectory, path, $"路径必须位于程序目录内：{path}");
        }
    }

    [TestMethod]
    public void DataDirectory_IsDataSubfolder()
    {
        var paths = new AppPaths(_root);

        Assert.AreEqual(Path.Combine(Path.GetFullPath(_root), "data"), paths.DataDirectory);
    }

    [TestMethod]
    public void Paths_DoNotReferenceUserProfile()
    {
        var paths = new AppPaths(_root);

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // 不允许把任何数据写到 %APPDATA% / %LOCALAPPDATA%
        foreach (string path in new[]
                 {
                     paths.DataDirectory,
                     paths.SettingsFile,
                     paths.LogDirectory,
                     paths.TempDirectory,
                 })
        {
            if (!string.IsNullOrEmpty(appData))
            {
                Assert.DoesNotStartWith(path, appData, $"不得写入 APPDATA：{path}");
            }

            if (!string.IsNullOrEmpty(localAppData))
            {
                Assert.DoesNotStartWith(path, localAppData, $"不得写入 LOCALAPPDATA：{path}");
            }
        }
    }

    [TestMethod]
    public void EnsureWritable_CreatesDirectories()
    {
        var paths = new AppPaths(_root);

        paths.EnsureWritable();

        Assert.IsTrue(Directory.Exists(paths.DataDirectory));
        Assert.IsTrue(Directory.Exists(paths.LogDirectory));
        Assert.IsTrue(Directory.Exists(paths.TempDirectory));
        Assert.IsTrue(Directory.Exists(Path.Combine(paths.TempDirectory, "WPF")));
    }

    [TestMethod]
    public void EnsureWritable_LeavesNoProbeFiles()
    {
        var paths = new AppPaths(_root);

        paths.EnsureWritable();

        Assert.IsEmpty(Directory.GetFiles(paths.DataDirectory, ".write-probe-*"));
        Assert.IsEmpty(Directory.GetFiles(paths.TempDirectory, ".write-probe-*"));
    }

    [TestMethod]
    public void Initialize_SetsCurrent()
    {
        AppPaths initialized = AppPaths.Initialize(_root);

        Assert.AreSame(initialized, AppPaths.Current);
    }

    [TestMethod]
    public void Constructor_RejectsBlankBaseDirectory()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new AppPaths("   "));
    }
}
