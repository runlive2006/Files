using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Files.Core.Models;
using Files.Core.Services;

namespace Files.App.ViewModels;

/// <summary>
/// 单个标签的浏览状态与行为。
/// </summary>
/// <remarks>
/// 一个 <see cref="TabViewModel"/> 对应一个 <see cref="TabState"/>。
/// <para>
/// <b>内存约束</b>：非活动标签必须只保留"冷状态"（路径、排序、滚动、选择），
/// 不得持有视图或项容器。界面层只维护<b>一个</b> <c>ListView</c>，
/// 切换标签时替换它的 <c>ItemsSource</c>，从而让 WPF 回收旧容器。
/// 若给每个标签各建一套视图，四栏 × 5 标签会直接击穿内存预算。
/// </para>
/// </remarks>
public sealed class TabViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IDirectoryLister _lister;
    private readonly LatestTaskRunner _runner = new();
    private readonly NavigationHistory _history = new();

    private DirectoryListing? _listing;
    private string _addressText = string.Empty;
    private string _statusText = "就绪";
    private string _title = string.Empty;
    private bool _isBusy;
    private bool _isActive;

    /// <summary>创建标签视图模型。</summary>
    public TabViewModel(TabState state, IDirectoryLister lister)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(lister);

        State = state;
        _lister = lister;

        if (Enum.TryParse(state.SortColumn, ignoreCase: true, out EntrySortColumn column))
        {
            SortColumn = column;
        }

        SortDescending = state.SortDescending;
        ViewMode = state.ViewMode;
        _title = TabTitle.GetShortName(state.Path);

        // 恢复历史栈（会话恢复时用）
        foreach (string path in state.BackStack)
        {
            _history.Navigate(path);
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>状态文本变化时触发。</summary>
    public event EventHandler? StatusChanged;

    /// <summary>对应的冷状态。</summary>
    public TabState State { get; }

    /// <summary>列表行。</summary>
    public ObservableCollection<EntryRow> Rows { get; } = [];

    /// <summary>标签标题（目录名，必要时带父目录消歧）。</summary>
    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    /// <summary>当前目录。</summary>
    public string CurrentPath => State.Path;

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

    /// <summary>是否为所在栏的活动标签。</summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
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

    /// <summary>排序依据。</summary>
    public EntrySortColumn SortColumn { get; private set; } = EntrySortColumn.Name;

    /// <summary>是否降序。</summary>
    public bool SortDescending { get; private set; }

    /// <summary>视图模式。</summary>
    public ViewMode ViewMode { get; private set; } = ViewMode.Details;

    /// <summary>条目数量。</summary>
    public int ItemCount => _listing?.Count ?? 0;

    /// <summary>当前列举结果。</summary>
    public DirectoryListing? Listing => _listing;

    /// <summary>设置状态文本。</summary>
    public void SetStatus(string message) => StatusText = message;

    /// <summary>导航到指定路径。</summary>
    public async Task NavigateAsync(string path, bool recordHistory = true)
    {
        IsBusy = true;
        StatusText = "正在读取…";

        EntrySortColumn column = SortColumn;
        bool descending = SortDescending;

        try
        {
            DirectoryListing? listing = await _runner.RunAsync(_ =>
            {
                DirectoryListing raw = _lister.List(path);
                return EntrySorter.Sort(raw, column, descending);
            });

            if (listing is null)
            {
                // 被更新的一次导航取代
                return;
            }

            _listing = listing;
            State.Path = listing.DirectoryPath;

            if (recordHistory)
            {
                _history.Navigate(listing.DirectoryPath);
            }

            RebuildRows(listing);
            AddressText = listing.DirectoryPath;
            Title = TabTitle.GetShortName(listing.DirectoryPath);
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

    /// <summary>按列排序（只重排现有结果，不重读目录）。</summary>
    public void ApplySort(EntrySortColumn column, bool descending)
    {
        SortColumn = column;
        SortDescending = descending;

        State.SortColumn = column.ToString();
        State.SortDescending = descending;

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
        State.ViewMode = mode;
    }

    /// <summary>记录滚动位置（切换标签前调用）。</summary>
    public void CaptureScrollOffset(double offset) => State.ScrollOffset = offset;

    /// <summary>记录当前选择集（切换标签前调用）。</summary>
    public void CaptureSelection(IEnumerable<string> selectedPaths)
    {
        State.SelectedPaths.Clear();
        State.SelectedPaths.AddRange(selectedPaths);
    }

    /// <summary>把状态同步回 <see cref="TabState"/>（关闭窗口时调用）。</summary>
    public void PersistState()
    {
        State.Path = CurrentPath;
        State.ViewMode = ViewMode;
        State.SortColumn = SortColumn.ToString();
        State.SortDescending = SortDescending;
        State.BackStack.Clear();
        State.BackStack.AddRange(_history.BackStack);
        State.ForwardStack.Clear();
        State.ForwardStack.AddRange(_history.ForwardStack);
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
