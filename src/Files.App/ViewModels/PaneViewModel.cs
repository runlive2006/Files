using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Files.App.ViewModels;
using Files.Core.Models;
using Files.Core.Services;

namespace Files.App.ViewModels;

/// <summary>
/// 单个栏的浏览状态与行为。
/// </summary>
/// <remarks>
/// 一个 <see cref="PaneViewModel"/> 对应布局树里的一个叶子（<see cref="PaneState"/>）。
/// 切换布局时，焦点栏的 <see cref="PaneState"/> 被原样复用，因此它的路径、
/// 选择与滚动都不会丢失（见 <see cref="LayoutFactory.Create"/>）。
/// </remarks>
public sealed class PaneViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IDirectoryLister _lister;
    private readonly LatestTaskRunner _runner = new();
    private readonly NavigationHistory _history = new();

    private DirectoryListing? _listing;
    private string _addressText = string.Empty;
    private string _statusText = "就绪";
    private bool _isFocused;
    private bool _isBusy;

    /// <summary>创建栏视图模型。</summary>
    public PaneViewModel(PaneState state, IDirectoryLister lister)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(lister);

        State = state;
        _lister = lister;

        if (Enum.TryParse(state.ActiveTab.SortColumn, ignoreCase: true, out EntrySortColumn column))
        {
            SortColumn = column;
        }

        SortDescending = state.ActiveTab.SortDescending;
        ViewMode = state.ActiveTab.ViewMode;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>状态文本变化时触发（供窗口状态栏订阅）。</summary>
    public event EventHandler? StatusChanged;

    /// <summary>对应的布局状态。</summary>
    public PaneState State { get; }

    /// <summary>列表行。</summary>
    public ObservableCollection<EntryRow> Rows { get; } = [];

    /// <summary>当前目录。</summary>
    public string CurrentPath => State.ActiveTab.Path;

    /// <summary>地址栏文本。</summary>
    public string AddressText
    {
        get => _addressText;
        set => SetField(ref _addressText, value);
    }

    /// <summary>状态文本。</summary>
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetField(ref _statusText, value))
            {
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// 设置状态文本（供视图层报告操作结果）。
    /// </summary>
    /// <remarks>
    /// 用方法而不是公开 setter：状态文本应当由 VM 与视图通过明确入口更新，
    /// 而不是允许任意赋值。
    /// </remarks>
    public void SetStatus(string message) => StatusText = message;

    /// <summary>是否为焦点栏。</summary>
    public bool IsFocused
    {
        get => _isFocused;
        set => SetField(ref _isFocused, value);
    }

    /// <summary>是否正在读取目录。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    /// <summary>是否可后退。</summary>
    public bool CanGoBack => _history.CanGoBack;

    /// <summary>是否可前进。</summary>
    public bool CanGoForward => _history.CanGoForward;

    /// <summary>当前排序依据的列。</summary>
    public EntrySortColumn SortColumn { get; private set; } = EntrySortColumn.Name;

    /// <summary>是否降序。</summary>
    public bool SortDescending { get; private set; }

    /// <summary>视图模式。</summary>
    public ViewMode ViewMode { get; set; } = ViewMode.Details;

    /// <summary>条目数量。</summary>
    public int ItemCount => _listing?.Count ?? 0;

    /// <summary>当前列举结果。</summary>
    public DirectoryListing? Listing => _listing;

    /// <summary>导航到指定路径。</summary>
    /// <param name="path">目标路径。</param>
    /// <param name="recordHistory">是否写入历史。</param>
    public async Task NavigateAsync(string path, bool recordHistory = true)
    {
        IsBusy = true;
        StatusText = "正在读取…";

        EntrySortColumn column = SortColumn;
        bool descending = SortDescending;

        try
        {
            // 列举与排序都在后台线程完成；期间若又发起导航，本次会被取消
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

            _listing = listing;
            State.ActiveTab.Path = listing.DirectoryPath;

            if (recordHistory)
            {
                _history.Navigate(listing.DirectoryPath);
            }

            RebuildRows(listing);
            AddressText = listing.DirectoryPath;
            NotifyNavigationState();
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException
                                       or UnauthorizedAccessException
                                       or IOException
                                       or ArgumentException)
        {
            StatusText = $"无法打开：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>重新读取当前目录。</summary>
    public Task RefreshAsync() => NavigateAsync(CurrentPath, recordHistory: false);

    /// <summary>后退。</summary>
    public Task GoBackAsync()
    {
        string? target = _history.Back();
        return target is null ? Task.CompletedTask : NavigateAsync(target, recordHistory: false);
    }

    /// <summary>前进。</summary>
    public Task GoForwardAsync()
    {
        string? target = _history.Forward();
        return target is null ? Task.CompletedTask : NavigateAsync(target, recordHistory: false);
    }

    /// <summary>返回上级目录。</summary>
    public Task GoUpAsync()
    {
        var parent = Directory.GetParent(CurrentPath);
        return parent is null ? Task.CompletedTask : NavigateAsync(parent.FullName);
    }

    /// <summary>按列排序（不重新读取目录，只重排现有结果）。</summary>
    public void ApplySort(EntrySortColumn column, bool descending)
    {
        SortColumn = column;
        SortDescending = descending;

        State.ActiveTab.SortColumn = column.ToString();
        State.ActiveTab.SortDescending = descending;

        if (_listing is null)
        {
            return;
        }

        _listing = EntrySorter.Sort(_listing, column, descending);
        RebuildRows(_listing);
    }

    /// <summary>应用视图模式。</summary>
    public void ApplyViewMode(ViewMode mode)
    {
        ViewMode = mode;
        State.ActiveTab.ViewMode = mode;
    }

    /// <summary>把当前路径与视图模式同步进布局状态（关闭窗口时调用）。</summary>
    public void PersistState()
    {
        State.ActiveTab.Path = CurrentPath;
        State.ActiveTab.ViewMode = ViewMode;
        State.ActiveTab.SortColumn = SortColumn.ToString();
        State.ActiveTab.SortDescending = SortDescending;
    }

    /// <inheritdoc />
    public void Dispose() => _runner.Dispose();

    private void RebuildRows(DirectoryListing listing)
    {
        long start = Stopwatch.GetTimestamp();

        Rows.Clear();
        for (int i = 0; i < listing.Count; i++)
        {
            Rows.Add(EntryRow.From(listing, i));
        }

        double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        StatusText = $"{listing.Count:N0} 个项目 · 读取 {elapsedMs:F1}ms";
    }

    private void NotifyNavigationState()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(ItemCount));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
