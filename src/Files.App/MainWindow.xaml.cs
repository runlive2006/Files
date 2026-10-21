using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Files.App.Diagnostics;
using Files.Core.Diagnostics;
using Files.Core.Models;
using Files.Core.Services;
using Files.Interop;

namespace Files.App;

/// <summary>
/// 文件管理器主窗口：地址栏导航、后退/前进、可排序列、两种视图模式。
/// </summary>
public partial class MainWindow : Window
{
    private readonly IDirectoryLister _lister = new Win32DirectoryLister();
    private readonly LatestTaskRunner _runner = new();
    private readonly NavigationHistory _history = new();
    private readonly SettingsService _settings;
    private readonly ObservableCollection<EntryRow> _rows = [];
    private readonly FrameTimer _frameTimer = new();
    private readonly WorkingSetSampler _memory = new();
    private readonly DispatcherTimer _hudTimer;
    private readonly bool _perfHudEnabled;
    private readonly GridView _detailsView;
    private readonly DataTemplate _listTemplate;

    private DirectoryListing? _currentListing;
    private string _currentPath = string.Empty;
    private EntrySortColumn _sortColumn = EntrySortColumn.Name;
    private bool _sortDescending;

    public MainWindow()
    {
        InitializeComponent();

        _perfHudEnabled = Environment.GetCommandLineArgs()
            .Any(arg => string.Equals(arg, "--perf", StringComparison.OrdinalIgnoreCase));

        _detailsView = (GridView)EntriesList.View;
        _listTemplate = (DataTemplate)FindResource("ListTemplate");

        _settings = new SettingsService(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Files",
            "settings.json"));
        _settings.Load();

        RestoreSettings();

        EntriesList.ItemsSource = _rows;
        AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnColumnHeaderClick));

        _hudTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _hudTimer.Tick += OnHudTick;

        Loaded += OnLoaded;
        Closed += OnClosed;

        string startPath = ResolveStartPath();
        _ = NavigateAsync(startPath, recordHistory: true);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
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
        _runner.Dispose();

        CaptureSettings();
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[settings] 保存失败：{ex.Message}");
        }
    }

    // ---------- 导航 ----------

    private string ResolveStartPath()
    {
        if (!string.IsNullOrWhiteSpace(_settings.Current.LastPath)
            && Directory.Exists(_settings.Current.LastPath))
        {
            return _settings.Current.LastPath;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private async Task NavigateAsync(string path, bool recordHistory)
    {
        StatusText.Text = "正在读取…";

        try
        {
            EntrySortColumn column = _sortColumn;
            bool descending = _sortDescending;

            // 列举与排序都在后台线程完成；若期间又发起了新的导航，本次会被取消
            DirectoryListing? listing = await _runner.RunAsync(_ =>
            {
                DirectoryListing raw = _lister.List(path);
                return EntrySorter.Sort(raw, column, descending);
            });

            if (listing is null)
            {
                // 被更新的一次导航取代，静默丢弃
                return;
            }

            _currentListing = listing;
            _currentPath = listing.DirectoryPath;

            if (recordHistory)
            {
                _history.Navigate(_currentPath);
            }

            RebuildRows(listing);
            AddressBox.Text = _currentPath;
            UpdateNavigationState();
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException
                                       or UnauthorizedAccessException
                                       or IOException
                                       or ArgumentException)
        {
            StatusText.Text = $"无法打开：{ex.Message}";
        }
    }

    private void RebuildRows(DirectoryListing listing)
    {
        long start = Stopwatch.GetTimestamp();

        _rows.Clear();
        for (int i = 0; i < listing.Count; i++)
        {
            _rows.Add(EntryRow.From(listing, i));
        }

        double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        StatusText.Text = $"{listing.Count:N0} 个项目 · 读取 {elapsedMs:F1}ms · {Path.GetFileName(listing.DirectoryPath) switch { "" => listing.DirectoryPath, var n => n }}";
    }

    private void UpdateNavigationState()
    {
        BackButton.IsEnabled = _history.CanGoBack;
        ForwardButton.IsEnabled = _history.CanGoForward;
        UpButton.IsEnabled = Directory.GetParent(_currentPath) is not null;
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        string? target = _history.Back();
        if (target is not null)
        {
            _ = NavigateAsync(target, recordHistory: false);
        }
    }

    private void OnForwardClick(object sender, RoutedEventArgs e)
    {
        string? target = _history.Forward();
        if (target is not null)
        {
            _ = NavigateAsync(target, recordHistory: false);
        }
    }

    private void OnGoUpClick(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_currentPath);
        if (parent is not null)
        {
            _ = NavigateAsync(parent.FullName, recordHistory: true);
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentPath))
        {
            _ = NavigateAsync(_currentPath, recordHistory: false);
        }
    }

    private void OnEntryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EntriesList.SelectedItem is not EntryRow { IsDirectory: true } row)
        {
            return;
        }

        _ = NavigateAsync(row.FullPath, recordHistory: true);
    }

    // ---------- 地址栏 ----------

    private void OnAddressKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string target = PathUtility.Expand(AddressBox.Text);

            if (string.IsNullOrWhiteSpace(target))
            {
                AddressBox.Text = _currentPath;
                return;
            }

            e.Handled = true;
            _ = NavigateAsync(target, recordHistory: true);
        }
        else if (e.Key == Key.Escape)
        {
            AddressBox.Text = _currentPath;
            EntriesList.Focus();
            e.Handled = true;
        }
    }

    private void OnAddressGotFocus(object sender, KeyboardFocusChangedEventArgs e) => AddressBox.SelectAll();

    private void OnAddressLostFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        AddressBox.Text = _currentPath;

    // ---------- 排序 ----------

    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column: { } column } header
            || header.Role == GridViewColumnHeaderRole.Padding)
        {
            return;
        }

        if (ColumnSort.GetKey(column) is not string tag
            || !Enum.TryParse(tag, ignoreCase: true, out EntrySortColumn parsed))
        {
            return;
        }

        if (parsed == _sortColumn)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = parsed;
            _sortDescending = false;
        }

        ResolveSort(parsed, _sortDescending);
    }

    private void ResolveSort(EntrySortColumn column, bool descending)
    {
        if (_currentListing is null)
        {
            return;
        }

        _currentListing = EntrySorter.Sort(_currentListing, column, descending);
        RebuildRows(_currentListing);
    }

    // ---------- 视图模式 ----------

    private void OnViewModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModeBox.SelectedItem is not ComboBoxItem { Tag: string mode })
        {
            return;
        }

        ApplyViewMode(mode);
    }

    private void ApplyViewMode(string mode)
    {
        if (string.Equals(mode, "List", StringComparison.OrdinalIgnoreCase))
        {
            EntriesList.View = null;
            EntriesList.ItemTemplate = _listTemplate;
        }
        else
        {
            EntriesList.View = _detailsView;
            EntriesList.ItemTemplate = null;
        }
    }

    // ---------- 键盘 ----------

    private void OnListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            EntriesList.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.I && Keyboard.Modifiers == ModifierKeys.Control)
        {
            InvertSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            OnRefreshClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            OnBackClick(sender, e);
            e.Handled = true;
        }
    }

    private void InvertSelection()
    {
        var selected = EntriesList.SelectedItems.Cast<EntryRow>().ToHashSet();

        foreach (EntryRow row in _rows)
        {
            if (selected.Contains(row))
            {
                EntriesList.SelectedItems.Remove(row);
            }
            else
            {
                EntriesList.SelectedItems.Add(row);
            }
        }
    }

    // ---------- 性能 HUD ----------

    private void OnHudTick(object? sender, EventArgs e)
    {
        PerfText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"FPS {_frameTimer.CurrentFps:F0} | 最差帧 {_frameTimer.WorstFrameMs:F0}ms | 内存 {_memory.Sample():F0}MB | 项 {_rows.Count:N0}");
    }

    // ---------- 设置 ----------

    private void RestoreSettings()
    {
        AppSettings settings = _settings.Current;

        if (!double.IsNaN(settings.Window.Width) && settings.Window.Width > 200)
        {
            Width = settings.Window.Width;
        }

        if (!double.IsNaN(settings.Window.Height) && settings.Window.Height > 200)
        {
            Height = settings.Window.Height;
        }

        if (settings.Window.Left is double savedLeft && settings.Window.Top is double savedTop)
        {
            // 只有当保存的位置仍在可见范围内才恢复，避免多显示器拔掉后窗口跑到屏幕外
            double virtualLeft = SystemParameters.VirtualScreenLeft;
            double virtualTop = SystemParameters.VirtualScreenTop;
            double virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
            double virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

            if (savedLeft >= virtualLeft && savedLeft < virtualRight
                && savedTop >= virtualTop && savedTop < virtualBottom)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = savedLeft;
                Top = savedTop;
            }
        }

        if (settings.Window.Maximized)
        {
            WindowState = WindowState.Maximized;
        }

        if (Enum.TryParse(settings.SortColumn, ignoreCase: true, out EntrySortColumn column))
        {
            _sortColumn = column;
        }

        _sortDescending = settings.SortDescending;

        foreach (var pair in settings.ColumnWidths)
        {
            var targetColumn = _detailsView.Columns
                .FirstOrDefault(c => string.Equals(ColumnSort.GetKey(c), pair.Key, StringComparison.OrdinalIgnoreCase));

            if (targetColumn is not null && pair.Value >= 40)
            {
                targetColumn.Width = pair.Value;
            }
        }

        string viewMode = string.IsNullOrWhiteSpace(settings.ViewMode) ? "Details" : settings.ViewMode;
        foreach (ComboBoxItem item in ViewModeBox.Items)
        {
            if (string.Equals(item.Tag as string, viewMode, StringComparison.OrdinalIgnoreCase))
            {
                ViewModeBox.SelectedItem = item;
                break;
            }
        }

        ApplyViewMode(viewMode);
    }

    private void CaptureSettings()
    {
        AppSettings settings = _settings.Current;

        settings.Window.Maximized = WindowState == WindowState.Maximized;

        if (WindowState == WindowState.Normal)
        {
            settings.Window.Width = Width;
            settings.Window.Height = Height;
            settings.Window.Left = Left;
            settings.Window.Top = Top;
        }

        settings.SortColumn = _sortColumn.ToString();
        settings.SortDescending = _sortDescending;
        settings.LastPath = _currentPath;

        if (ViewModeBox.SelectedItem is ComboBoxItem { Tag: string mode })
        {
            settings.ViewMode = mode;
        }

        settings.ColumnWidths.Clear();
        foreach (var column in _detailsView.Columns)
        {
            if (ColumnSort.GetKey(column) is string tag && column.Width >= 40)
            {
                settings.ColumnWidths[tag] = column.Width;
            }
        }
    }

    /// <summary>供列表绑定使用的视图行。</summary>
    private sealed record EntryRow(
        string Name,
        string FullPath,
        bool IsDirectory,
        string Kind,
        string SizeText,
        string LastModifiedText,
        Brush IconBrush)
    {
        public static EntryRow From(DirectoryListing listing, int index)
        {
            FileSystemEntry entry = listing.Entries[index];

            return new EntryRow(
                entry.Name,
                listing.GetFullPath(entry),
                entry.IsDirectory,
                DescribeKind(entry),
                entry.IsDirectory ? string.Empty : FormatSize(entry.SizeBytes),
                entry.LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture),
                IconBrushes.For(entry));
        }

        private static string DescribeKind(FileSystemEntry entry)
        {
            if (entry.IsDirectory)
            {
                return "文件夹";
            }

            string extension = entry.Extension;
            return string.IsNullOrEmpty(extension) ? "文件" : extension;
        }

        private static string FormatSize(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
        };
    }

    /// <summary>
    /// 占位的图标着色。真实 Shell 图标（<c>SHGetFileInfo</c> / <c>IImageList</c>）
    /// 属于 M3 任务 3.5，这里先用按类型着色的方块，避免过早引入逐项图标开销。
    /// </summary>
    private static class IconBrushes
    {
        private static readonly Brush Folder = Freeze(Color.FromRgb(0xE8, 0xB4, 0x3C));
        private static readonly Brush Image = Freeze(Color.FromRgb(0x4C, 0xA0, 0xE0));
        private static readonly Brush Code = Freeze(Color.FromRgb(0x6C, 0xC0, 0x6C));
        private static readonly Brush Document = Freeze(Color.FromRgb(0x9A, 0x9A, 0x9A));
        private static readonly Brush Other = Freeze(Color.FromRgb(0xB0, 0xB0, 0xB0));

        internal static Brush For(FileSystemEntry entry)
        {
            if (entry.IsDirectory)
            {
                return Folder;
            }

            return entry.Extension.ToLowerInvariant() switch
            {
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => Image,
                ".cs" or ".js" or ".ts" or ".py" or ".cpp" or ".h" or ".json" or ".xml" => Code,
                ".txt" or ".md" or ".log" or ".pdf" or ".docx" or ".xlsx" => Document,
                _ => Other,
            };
        }

        private static Brush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
