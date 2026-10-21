using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Files.App.Diagnostics;
using Files.Core.Diagnostics;
using Files.Core.Models;
using Files.Core.Services;
using Files.Interop;

namespace Files.App;

/// <summary>
/// 文件管理器主窗口：展示当前目录内容，支持进入子目录与返回上级。
/// </summary>
public partial class MainWindow : Window
{
    private readonly IDirectoryLister _lister = new Win32DirectoryLister();
    private readonly ObservableCollection<EntryRow> _rows = [];
    private readonly FrameTimer _frameTimer = new();
    private readonly WorkingSetSampler _memory = new();
    private readonly DispatcherTimer _hudTimer;
    private readonly bool _perfHudEnabled;

    private string _currentPath = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        _perfHudEnabled = Environment.GetCommandLineArgs()
            .Any(arg => string.Equals(arg, "--perf", StringComparison.OrdinalIgnoreCase));

        EntriesList.ItemsSource = _rows;

        // 性能 HUD 每 500ms 刷新一次；未启用时不创建定时器，避免空转
        _hudTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _hudTimer.Tick += OnHudTick;

        Loaded += OnLoaded;
        Closed += OnClosed;

        Navigate(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 首屏渲染完成打点：放到 Loaded 之后的下一个渲染周期，确保计的是"看得见"的时刻
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            StartupProbe.Mark("first-render");

            if (_perfHudEnabled)
            {
                _frameTimer.Start();
                _hudTimer.Start();
            }

            Debug.WriteLine($"[perf] startup: {StartupProbe.Report()}");
        });
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _hudTimer.Stop();
        _frameTimer.Stop();
    }

    private void OnHudTick(object? sender, EventArgs e)
    {
        double fps = _frameTimer.CurrentFps;
        double worst = _frameTimer.WorstFrameMs;
        double memory = _memory.Sample();

        PerfText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"FPS {fps:F0} | 最差帧 {worst:F0}ms | 内存 {memory:F0}MB | 项 {_rows.Count}");
    }

    private void OnGoUpClick(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_currentPath);
        if (parent is not null)
        {
            Navigate(parent.FullName);
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Navigate(_currentPath);

    private void OnEntryDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (EntriesList.SelectedItem is not EntryRow { IsDirectory: true } row)
        {
            return;
        }

        Navigate(row.FullPath);
    }

    private void Navigate(string path)
    {
        try
        {
            long startTimestamp = Stopwatch.GetTimestamp();

            IReadOnlyList<FileSystemEntry> entries = _lister.List(path);

            _currentPath = path;
            _rows.Clear();
            foreach (var entry in entries)
            {
                _rows.Add(EntryRow.From(entry));
            }

            double elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            CurrentPathText.Text = path;
            StatusText.Text = $"{entries.Count} 个项目 · 列举 {elapsedMs:F1}ms";
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            StatusText.Text = $"无法打开：{ex.Message}";
        }
    }

    /// <summary>供列表绑定使用的视图行。</summary>
    private sealed record EntryRow(
        string Name,
        string FullPath,
        bool IsDirectory,
        string Kind,
        string SizeText,
        string LastModifiedText)
    {
        public static EntryRow From(FileSystemEntry entry) => new(
            entry.Name,
            entry.FullPath,
            entry.IsDirectory,
            entry.IsDirectory ? "文件夹" : (string.IsNullOrEmpty(entry.Extension) ? "文件" : entry.Extension),
            entry.IsDirectory ? string.Empty : FormatSize(entry.SizeBytes),
            entry.LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));

        private static string FormatSize(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
        };
    }
}
