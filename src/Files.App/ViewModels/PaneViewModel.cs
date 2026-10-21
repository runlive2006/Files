using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Files.Core.Models;
using Files.Core.Services;

namespace Files.App.ViewModels;

/// <summary>
/// 单个栏：一个标签组，其中一个是活动标签。
/// </summary>
/// <remarks>
/// 切换布局时焦点栏的 <see cref="PaneState"/> 会被原样复用
/// （见 <see cref="LayoutFactory.Create"/>），因此该栏的标签集合与活动标签都不会丢失。
/// </remarks>
public sealed class PaneViewModel : INotifyPropertyChanged, IDisposable
{
    private const int MaxClosedTabHistory = 10;

    private readonly IDirectoryLister _lister;
    private IconProvider _icons;
    private readonly Stack<TabState> _closedTabs = new();
    private bool _isFocused;

    /// <summary>创建栏视图模型。</summary>
    public PaneViewModel(PaneState state, IDirectoryLister lister, IconProvider icons)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(lister);
        ArgumentNullException.ThrowIfNull(icons);

        State = state;
        _lister = lister;
        _icons = icons;

        Tabs = [];
        foreach (TabState tabState in state.Tabs)
        {
            Tabs.Add(CreateTab(tabState));
        }

        ActiveIndex = Math.Clamp(state.ActiveTabIndex, 0, Math.Max(0, Tabs.Count - 1));
        SyncActiveFlags();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>标签集合变化时触发（用于刷新标签条可见性）。</summary>
    public event EventHandler? TabsChanged;

    /// <summary>对应的布局状态。</summary>
    public PaneState State { get; }

    /// <summary>标签集合。</summary>
    public ObservableCollection<TabViewModel> Tabs { get; }

    /// <summary>活动标签下标。</summary>
    public int ActiveIndex
    {
        get => State.ActiveTabIndex;
        set
        {
            if (value < 0 || value >= Tabs.Count || State.ActiveTabIndex == value)
            {
                return;
            }

            State.Activate(value);
            SyncActiveFlags();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveTab));
            OnPropertyChanged(nameof(StatusText));
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>活动标签。</summary>
    public TabViewModel ActiveTab => Tabs[Math.Clamp(ActiveIndex, 0, Math.Max(0, Tabs.Count - 1))];

    /// <summary>标签条是否可见（标签数 ≥2 时才显示，避免浪费垂直空间）。</summary>
    public bool ShowTabStrip => Tabs.Count >= 2;

    /// <summary>是否为焦点栏。</summary>
    public bool IsFocused
    {
        get => _isFocused;
        set => SetField(ref _isFocused, value);
    }

    /// <summary>活动标签的状态文本（转发）。</summary>
    public string StatusText => ActiveTab.StatusText;

    /// <summary>活动标签的当前路径。</summary>
    public string CurrentPath => ActiveTab.CurrentPath;

    /// <summary>活动标签的视图模式（转发）。</summary>
    public ViewMode ViewMode => ActiveTab.ViewMode;

    /// <summary>替换图标提供者（设置变更时调用），并同步到全部标签。</summary>
    public void ReplaceIconProvider(IconProvider icons)
    {
        ArgumentNullException.ThrowIfNull(icons);
        _icons = icons;

        foreach (TabViewModel tab in Tabs)
        {
            tab.ReplaceIconProvider(icons);
        }
    }
    /// <summary>把视图模式应用到活动标签。</summary>
    public void ApplyViewMode(ViewMode mode) => ActiveTab.ApplyViewMode(mode);

    /// <summary>状态文本变化时触发。</summary>
    public event EventHandler? StatusChanged;

    /// <summary>设置活动标签的状态文本。</summary>
    public void SetStatus(string message) => ActiveTab.SetStatus(message);

    /// <summary>在当前标签之后新建标签，路径继承当前标签。</summary>
    public TabViewModel AddTab(string? path = null)
    {
        string target = path ?? ActiveTab.CurrentPath;

        var tabState = new TabState
        {
            Path = target,
            ViewMode = ActiveTab.ViewMode,
            SortColumn = ActiveTab.SortColumn.ToString(),
            SortDescending = ActiveTab.SortDescending,
        };

        State.AddTab(tabState);
        TabViewModel viewModel = CreateTab(tabState);
        Tabs.Insert(State.ActiveTabIndex, viewModel);

        NotifyTabsChanged();
        OnPropertyChanged(nameof(ActiveTab));
        OnPropertyChanged(nameof(ActiveIndex));
        return viewModel;
    }

    /// <summary>关闭指定标签。至少保留一个。</summary>
    public bool CloseTab(TabViewModel tab)
    {
        int index = Tabs.IndexOf(tab);
        if (index < 0 || !State.CloseTab(index))
        {
            return false;
        }

        Tabs.RemoveAt(index);
        RememberClosed(tab.State);
        tab.Dispose();

        NotifyTabsChanged();
        OnPropertyChanged(nameof(ActiveTab));
        OnPropertyChanged(nameof(ActiveIndex));
        OnPropertyChanged(nameof(StatusText));
        return true;
    }

    /// <summary>关闭当前标签。</summary>
    public bool CloseActiveTab() => CloseTab(ActiveTab);

    /// <summary>恢复最近关闭的标签。</summary>
    public TabViewModel? ReopenClosedTab()
    {
        if (_closedTabs.Count == 0)
        {
            return null;
        }

        TabState restored = _closedTabs.Pop();
        State.AddTab(restored);

        TabViewModel viewModel = CreateTab(restored);
        Tabs.Insert(State.ActiveTabIndex, viewModel);

        NotifyTabsChanged();
        OnPropertyChanged(nameof(ActiveTab));
        OnPropertyChanged(nameof(ActiveIndex));
        return viewModel;
    }

    /// <summary>切换到下一个 / 上一个标签。</summary>
    public void CycleTab(int delta)
    {
        if (Tabs.Count <= 1)
        {
            return;
        }

        int next = (ActiveIndex + delta + Tabs.Count) % Tabs.Count;
        ActiveIndex = next;
    }

    /// <summary>刷新标签标题（存在同名目录时补充父目录名）。</summary>
    public void RefreshTitles()
    {
        IReadOnlyList<TabState> states = [.. Tabs.Select(t => t.State)];
        foreach (TabViewModel tab in Tabs)
        {
            tab.Title = TabTitle.Resolve(tab.State, states);
        }
    }

    /// <summary>导航活动标签。</summary>
    public Task NavigateAsync(string path, bool recordHistory = true) =>
        ActiveTab.NavigateAsync(path, recordHistory);

    /// <summary>刷新活动标签。</summary>
    public Task RefreshAsync() => ActiveTab.RefreshAsync();

    /// <summary>把全部标签的状态同步回模型（关闭窗口时调用）。</summary>
    public void PersistState()
    {
        foreach (TabViewModel tab in Tabs)
        {
            tab.PersistState();
        }

        State.ActiveTabIndex = ActiveIndex;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (TabViewModel tab in Tabs)
        {
            tab.Dispose();
        }
    }

    private TabViewModel CreateTab(TabState state)
    {
        var viewModel = new TabViewModel(state, _lister, _icons);
        viewModel.StatusChanged += (_, _) =>
        {
            if (ReferenceEquals(viewModel, ActiveTab))
            {
                OnPropertyChanged(nameof(StatusText));
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
        };

        return viewModel;
    }

    private void RememberClosed(TabState state)
    {
        _closedTabs.Push(state);

        // 只保留最近若干个，避免长期运行后无限增长
        if (_closedTabs.Count > MaxClosedTabHistory)
        {
            var kept = _closedTabs.Take(MaxClosedTabHistory).Reverse().ToArray();
            _closedTabs.Clear();
            foreach (TabState item in kept)
            {
                _closedTabs.Push(item);
            }
        }
    }

    private void NotifyTabsChanged()
    {
        SyncActiveFlags();
        OnPropertyChanged(nameof(ShowTabStrip));
        TabsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>把"是否为活动标签"同步到各标签，供标签条高亮。</summary>
    private void SyncActiveFlags()
    {
        int active = Math.Clamp(ActiveIndex, 0, Math.Max(0, Tabs.Count - 1));

        for (int i = 0; i < Tabs.Count; i++)
        {
            Tabs[i].IsActive = i == active;
        }
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

    private void OnPropertyChanged(string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
