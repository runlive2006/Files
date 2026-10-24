using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Files.App.Diagnostics;
using Files.App.ViewModels;
using Files.App.Views;
using Files.Core.Diagnostics;
using Files.Core.Models;
using Files.Core.Services;
using Files.Interop;

namespace Files.App;

/// <summary>
/// 主窗口：渲染布局树、管理焦点栏、转发全局命令。
/// </summary>
/// <remarks>
/// 多栏的关键设计：布局树是唯一事实来源（<see cref="LayoutNode"/>），
/// 每个叶子绑定一个 <see cref="PaneViewModel"/>。切换布局时通过
/// <see cref="LayoutFactory.Create"/> 复用焦点栏的状态，其余栏从它克隆，
/// 因此用户当前正在看的目录不会因为切换布局而丢失。
/// </remarks>
public partial class MainWindow : Window
{
    private readonly Win32DirectoryLister _lister = new();
    private IconProvider _icons = new(useSystemIcons: true);
    private readonly Dictionary<PaneState, PaneViewModel> _viewModels = [];
    private readonly FrameTimer _frameTimer = new();
    private readonly WorkingSetSampler _memory = new();
    private readonly DispatcherTimer _hudTimer;
    private readonly SettingsService _settings;
    private readonly bool _perfHudEnabled;

    private LayoutNode _layout = null!;
    private List<PaneView> _paneViews = [];
    private int _focusedIndex;

    public MainWindow()
    {
        InitializeComponent();

        _perfHudEnabled = Environment.GetCommandLineArgs()
            .Any(arg => string.Equals(arg, "--perf", StringComparison.OrdinalIgnoreCase));

        _settings = new SettingsService(
            AppPaths.Current?.SettingsFile
            ?? Path.Combine(AppContext.BaseDirectory, AppPaths.DataFolderName, "settings.json"));
        _settings.Load();

        // 图标开关先于任何栏创建，避免先建行再用错图标
        _icons = new IconProvider(useSystemIcons: true);
        _lister.IncludeHidden = _settings.Current.View.ShowHidden;

        MenuUseSystemIcons.IsChecked = true;

        _hudTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _hudTimer.Tick += OnHudTick;

        Loaded += OnLoaded;
        Closed += OnClosed;
        PreviewKeyDown += OnWindowPreviewKeyDown;

        RestoreLayout();
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

            if (_paneViews.Count > 0)
            {
                _paneViews[Math.Clamp(_focusedIndex, 0, _paneViews.Count - 1)].FocusList();
            }

            System.Diagnostics.Debug.WriteLine($"[perf] startup: {StartupProbe.Report()}");
        });
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _hudTimer.Stop();
        _frameTimer.Stop();

        CaptureSettings();

        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[settings] 保存失败：{ex.Message}");
        }

        foreach (PaneViewModel viewModel in _viewModels.Values)
        {
            viewModel.Dispose();
        }
    }

    // ---------- 布局 ----------

    private void RestoreLayout()
    {
        AppSettings settings = _settings.Current;

        if (!Enum.TryParse(settings.LayoutMode, ignoreCase: true, out PaneLayout mode))
        {
            mode = PaneLayout.Single;
        }

        _layout = LayoutFactory.Create(null, PaneLayout.Single);

        if (mode != PaneLayout.Single)
        {
            _layout = LayoutFactory.Create(_layout, mode);
        }

        ApplySavedPanePaths(settings);
        ApplySavedRatios(settings);

        RenderLayout();
        SyncLayoutMenu(mode);

        _ = NavigateAllPanesAsync();
    }

    private void ApplySavedPanePaths(AppSettings settings)
    {
        if (settings.Panes.Count == 0)
        {
            return;
        }

        IReadOnlyList<PaneState> panes = LayoutFactory.CollectPanes(_layout);
        for (int i = 0; i < panes.Count && i < settings.Panes.Count; i++)
        {
            PaneSettings saved = settings.Panes[i];

            // 恢复标签列表：逐个校验路径仍存在，避免启动到已删除目录
            if (saved.Tabs.Count > 0)
            {
                var restored = new List<TabState>(saved.Tabs.Count);
                foreach (string tabPath in saved.Tabs)
                {
                    if (!string.IsNullOrWhiteSpace(tabPath) && Directory.Exists(tabPath))
                    {
                        restored.Add(new TabState { Path = tabPath });
                    }
                }

                if (restored.Count > 0)
                {
                    panes[i].Tabs.Clear();
                    panes[i].Tabs.AddRange(restored);
                    panes[i].ActiveTabIndex = Math.Clamp(saved.ActiveTabIndex, 0, restored.Count - 1);
                }
            }
            else if (!string.IsNullOrWhiteSpace(saved.Path) && Directory.Exists(saved.Path))
            {
                panes[i].ActiveTab.Path = saved.Path;
            }

            if (Enum.TryParse(saved.ViewMode, ignoreCase: true, out ViewMode viewMode))
            {
                panes[i].ActiveTab.ViewMode = viewMode;
            }
        }
    }

    private void ApplySavedRatios(AppSettings settings)
    {
        if (settings.SplitRatios.Count == 0)
        {
            return;
        }

        IReadOnlyList<SplitNode> splits = LayoutFactory.CollectSplits(_layout);
        for (int i = 0; i < splits.Count && i < settings.SplitRatios.Count; i++)
        {
            double ratio = settings.SplitRatios[i];
            if (ratio is > 0.05 and < 0.95)
            {
                splits[i].FirstRatio = ratio;
            }
        }
    }

    private async Task NavigateAllPanesAsync()
    {
        IReadOnlyList<PaneState> panes = LayoutFactory.CollectPanes(_layout);

        var tasks = new List<Task>(panes.Count);
        foreach (PaneState pane in panes)
        {
            PaneViewModel viewModel = GetViewModel(pane);
            tasks.Add(viewModel.NavigateAsync(viewModel.CurrentPath));
        }

        await Task.WhenAll(tasks);
        UpdatePaneIndicator();
    }

    /// <summary>刷新所有栏的当前标签（用于跨栏操作后同步视图）。</summary>
    private async Task RefreshAllPanesAsync()
    {
        var tasks = new List<Task>(_paneViews.Count);
        foreach (PaneView view in _paneViews)
        {
            tasks.Add(view.ViewModel.RefreshAsync());
        }

        await Task.WhenAll(tasks);
    }

    private PaneViewModel GetViewModel(PaneState pane)
    {
        if (_viewModels.TryGetValue(pane, out PaneViewModel? existing))
        {
            return existing;
        }

        var viewModel = new PaneViewModel(pane, _lister, _icons);
        viewModel.StatusChanged += (_, _) =>
        {
            if (_focusedIndex >= 0 && _focusedIndex < _paneViews.Count
                && ReferenceEquals(_paneViews[_focusedIndex].ViewModel, viewModel))
            {
                StatusText.Text = viewModel.StatusText;
            }
        };

        _viewModels[pane] = viewModel;
        return viewModel;
    }

    /// <summary>重新渲染布局树。</summary>
    private void RenderLayout()
    {
        LayoutHost.Children.Clear();
        _paneViews = [];

        FrameworkElement root = BuildVisual(_layout);
        LayoutHost.Children.Add(root);

        // 布局变化后，不在树中的栏视图模型可以释放
        IReadOnlyList<PaneState> live = LayoutFactory.CollectPanes(_layout);
        foreach (PaneState pane in _viewModels.Keys.Except(live).ToList())
        {
            _viewModels[pane].Dispose();
            _viewModels.Remove(pane);
        }

        SetFocusedIndex(Math.Clamp(_focusedIndex, 0, Math.Max(0, _paneViews.Count - 1)));
    }

    private FrameworkElement BuildVisual(LayoutNode node)
    {
        if (node is PaneNode paneNode)
        {
            var view = new PaneView();
            view.Initialize(GetViewModel(paneNode.Pane), _icons);
            view.PaneActivated += (_, _) =>
            {
                int index = _paneViews.IndexOf(view);
                if (index >= 0)
                {
                    SetFocusedIndex(index);
                }
            };

            // 跨栏拖放移动后，源栏也需要刷新
            view.ContentChanged += async (_, _) => await RefreshAllPanesAsync();

            _paneViews.Add(view);
            return view;
        }

        var split = (SplitNode)node;
        var grid = new Grid();

        if (split.Direction == SplitDirection.Horizontal)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(split.FirstRatio, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - split.FirstRatio, GridUnitType.Star) });

            FrameworkElement first = BuildVisual(split.First);
            Grid.SetColumn(first, 0);
            grid.Children.Add(first);

            var splitter = CreateSplitter(isHorizontal: true);
            Grid.SetColumn(splitter, 1);
            grid.Children.Add(splitter);

            FrameworkElement second = BuildVisual(split.Second);
            Grid.SetColumn(second, 2);
            grid.Children.Add(second);

            splitter.DragCompleted += (_, _) =>
            {
                double total = grid.ColumnDefinitions[0].ActualWidth + grid.ColumnDefinitions[2].ActualWidth;
                if (total > 0)
                {
                    split.FirstRatio = grid.ColumnDefinitions[0].ActualWidth / total;
                }
            };
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(split.FirstRatio, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1 - split.FirstRatio, GridUnitType.Star) });

            FrameworkElement first = BuildVisual(split.First);
            Grid.SetRow(first, 0);
            grid.Children.Add(first);

            var splitter = CreateSplitter(isHorizontal: false);
            Grid.SetRow(splitter, 1);
            grid.Children.Add(splitter);

            FrameworkElement second = BuildVisual(split.Second);
            Grid.SetRow(second, 2);
            grid.Children.Add(second);

            splitter.DragCompleted += (_, _) =>
            {
                double total = grid.RowDefinitions[0].ActualHeight + grid.RowDefinitions[2].ActualHeight;
                if (total > 0)
                {
                    split.FirstRatio = grid.RowDefinitions[0].ActualHeight / total;
                }
            };
        }

        return grid;
    }

    private static GridSplitter CreateSplitter(bool isHorizontal)
    {
        return isHorizontal
            ? new GridSplitter
            {
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ResizeDirection = GridResizeDirection.Columns,
                Background = System.Windows.Media.Brushes.LightGray,
            }
            : new GridSplitter
            {
                Height = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ResizeDirection = GridResizeDirection.Rows,
                Background = System.Windows.Media.Brushes.LightGray,
            };
    }

    private void SetFocusedIndex(int index)
    {
        if (_paneViews.Count == 0)
        {
            _focusedIndex = 0;
            return;
        }

        _focusedIndex = Math.Clamp(index, 0, _paneViews.Count - 1);

        for (int i = 0; i < _paneViews.Count; i++)
        {
            _paneViews[i].ViewModel.IsFocused = i == _focusedIndex;
        }

        StatusText.Text = _paneViews[_focusedIndex].ViewModel.StatusText;
        SyncViewMenu(_paneViews[_focusedIndex].ViewModel.ViewMode);
        UpdatePaneIndicator();
    }

    private void UpdatePaneIndicator() =>
        PaneIndicatorText.Text = $"栏 {_focusedIndex + 1}/{Math.Max(1, _paneViews.Count)}";


    // ---------- 布局切换 ----------

    private void OnLayoutSingleClick(object sender, RoutedEventArgs e) => SwitchLayout(PaneLayout.Single);

    private void OnLayoutHorizontalClick(object sender, RoutedEventArgs e) => SwitchLayout(PaneLayout.HorizontalSplit);

    private void OnLayoutVerticalClick(object sender, RoutedEventArgs e) => SwitchLayout(PaneLayout.VerticalSplit);

    private void OnLayoutQuadClick(object sender, RoutedEventArgs e) => SwitchLayout(PaneLayout.Quad);

    private void SwitchLayout(PaneLayout target)
    {
        if (_paneViews.Count > 0)
        {
            _paneViews[_focusedIndex].ViewModel.PersistState();
        }

        _layout = LayoutFactory.Create(_layout, target, _focusedIndex);
        _focusedIndex = 0;

        RenderLayout();
        SyncLayoutMenu(target);

        _ = NavigateAllPanesAsync();
    }

    /// <summary>同步"分栏方式"菜单的勾选状态。</summary>
    private void SyncLayoutMenu(PaneLayout mode)
    {
        MenuLayoutSingle.IsChecked = mode == PaneLayout.Single;
        MenuLayoutHorizontal.IsChecked = mode == PaneLayout.HorizontalSplit;
        MenuLayoutVertical.IsChecked = mode == PaneLayout.VerticalSplit;
        MenuLayoutQuad.IsChecked = mode == PaneLayout.Quad;
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        ModifierKeys modifiers = Keyboard.Modifiers;

        if (modifiers == ModifierKeys.Control)
        {
            PaneLayout? target = e.Key switch
            {
                Key.D1 or Key.NumPad1 => PaneLayout.Single,
                Key.D2 or Key.NumPad2 => PaneLayout.HorizontalSplit,
                Key.D3 or Key.NumPad3 => PaneLayout.VerticalSplit,
                Key.D4 or Key.NumPad4 => PaneLayout.Quad,
                _ => null,
            };

            if (target is not null)
            {
                SwitchLayout(target.Value);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.F6 && _paneViews.Count > 1)
        {
            int delta = modifiers == ModifierKeys.Shift ? -1 : 1;
            int next = (_focusedIndex + delta + _paneViews.Count) % _paneViews.Count;

            SetFocusedIndex(next);
            _paneViews[next].FocusList();
            e.Handled = true;
            return;
        }

        // Ctrl+, 打开设置（与主流应用一致）
        if (modifiers == ModifierKeys.Control && e.Key == Key.OemComma)
        {
            OpenSettings();
            e.Handled = true;
            return;
        }

        // 标签：Ctrl+Tab / Ctrl+Shift+Tab 切换，Ctrl+Shift+T 恢复最近关闭
        if (modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Tab)
        {
            FocusedPane?.CycleTab(modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.T)
        {
            _ = FocusedPane?.ReopenClosedTabAsync();
            e.Handled = true;
        }
    }

    // ---------- 全局命令（作用于焦点栏） ----------

    private PaneView? FocusedPane =>
        _focusedIndex >= 0 && _focusedIndex < _paneViews.Count ? _paneViews[_focusedIndex] : null;

    private void OnNewFolderClick(object sender, RoutedEventArgs e) => _ = FocusedPane?.NewFolderAsync();

    private void OnRenameClick(object sender, RoutedEventArgs e) => _ = FocusedPane?.RenameAsync();

    private void OnDeleteClick(object sender, RoutedEventArgs e) => _ = FocusedPane?.DeleteAsync(DeleteMode.Recycle);

    private void OnCopyClick(object sender, RoutedEventArgs e) => FocusedPane?.CopySelection();

    private void OnCutClick(object sender, RoutedEventArgs e) => FocusedPane?.CutSelection();

    private void OnPasteClick(object sender, RoutedEventArgs e) => _ = FocusedPane?.PasteAsync();

    // ---------- 视图菜单 ----------

    private void OnViewDetailsClick(object sender, RoutedEventArgs e) => SetViewMode(ViewMode.Details);

    private void OnViewListClick(object sender, RoutedEventArgs e) => SetViewMode(ViewMode.List);

    private void OnViewThumbnailsClick(object sender, RoutedEventArgs e) => SetViewMode(ViewMode.Thumbnails);

    private void SetViewMode(ViewMode mode)
    {
        PaneView? pane = FocusedPane;
        if (pane is null)
        {
            return;
        }

        pane.ViewModel.ApplyViewMode(mode);
        pane.ApplyViewMode(mode);

        // 缩略图用大图标，其余用 16×16；切换后需要重读以重建行
        _ = pane.ReloadIconsAsync();

        SyncViewMenu(mode);
    }

    private void SyncViewMenu(ViewMode mode)
    {
        MenuViewDetails.IsChecked = mode == ViewMode.Details;
        MenuViewList.IsChecked = mode == ViewMode.List;
        MenuViewThumbnails.IsChecked = mode == ViewMode.Thumbnails;
    }

    // ---------- 设置菜单 ----------

    private void OnToggleSystemIconsClick(object sender, RoutedEventArgs e)
    {
        bool enabled = MenuUseSystemIcons.IsChecked;
        // 系统图标开关已迁移到设置窗口（后续接入）；此处仅即时生效

        // 图标在行构造时确定，切换后必须整体重建
        _icons = new IconProvider(enabled);

        foreach (PaneView view in _paneViews)
        {
            view.ReplaceIconProvider(_icons);
            _ = view.ReloadIconsAsync();
        }

        StatusText.Text = enabled ? "已启用系统图标" : "已改用占位图标";
    }

    // ---------- 设置窗口 ----------

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        var window = new Views.SettingsWindow(_settings) { Owner = this };
        window.SettingChanged += OnSettingChanged;
        window.ShowDialog();
    }

    /// <summary>
    /// 应用某项设置变更。
    /// </summary>
    /// <remarks>
    /// 按代价分档（设计文档 SETTINGS §5.6）：
    /// 视图类改动立即作用到所有栏；需要重新枚举的项走后台刷新，期间界面保持可用。
    /// </remarks>
    private void OnSettingChanged(object? sender, string key)
    {
        switch (key)
        {
            case "View.ShowHidden":
                // C 档：隐藏属性只有枚举时才知道，必须重读目录
                _lister.IncludeHidden = _settings.Current.View.ShowHidden;
                _ = RefreshAllPanesAsync();
                StatusText.Text = "已更新隐藏文件显示，正在刷新…";
                break;

            case "View.ShowExtensions":
            case "View.IconSize":
            case "View.RowHeight":
            case "View.AlternateRowColor":
            case "View.ShowGridLines":
            case "View.FolderFirst":
            case "View.ShowStatusBar":
            case "Perf.ShowPerfHud":
                // A/B 档：只需重设视图参数，不重读目录
                _ = RefreshAllPanesAsync();
                break;

            case "View.ThumbnailSize":
                _ = RefreshAllPanesAsync();
                break;

            default:
                // 其余项在下次启动或下次操作时生效，无需即时刷新
                break;
        }
    }
    // ---------- 性能 HUD ----------

    private void OnHudTick(object? sender, EventArgs e)
    {
        PerfText.Text = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"FPS {_frameTimer.CurrentFps:F0} | 最差帧 {_frameTimer.WorstFrameMs:F0}ms | 内存 {_memory.Sample():F0}MB | 栏 {_paneViews.Count}");
    }

    // ---------- 设置 ----------

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

        settings.LayoutMode = DetectLayoutMode();

        settings.Panes.Clear();
        foreach (PaneState pane in LayoutFactory.CollectPanes(_layout))
        {
            if (_viewModels.TryGetValue(pane, out PaneViewModel? viewModel))
            {
                viewModel.PersistState();
            }

            settings.Panes.Add(new PaneSettings
            {
                Path = pane.ActiveTab.Path,
                ViewMode = pane.ActiveTab.ViewMode.ToString(),
                Tabs = [.. pane.Tabs.Select(t => t.Path)],
                ActiveTabIndex = pane.ActiveTabIndex,
            });
        }

        settings.SplitRatios.Clear();
        foreach (SplitNode split in LayoutFactory.CollectSplits(_layout))
        {
            settings.SplitRatios.Add(split.FirstRatio);
        }

        // 上次路径已由 Panes[0].Path 承载，不再单独保存
    }

    /// <summary>从布局树反推布局模式（用于持久化）。</summary>
    private string DetectLayoutMode()
    {
        if (_layout is PaneNode)
        {
            return PaneLayout.Single.ToString();
        }

        if (_layout is SplitNode root && root.First is SplitNode)
        {
            // 两个子节点都分裂 → 田字四栏
            return PaneLayout.Quad.ToString();
        }

        return _layout is SplitNode { Direction: SplitDirection.Horizontal }
            ? PaneLayout.HorizontalSplit.ToString()
            : PaneLayout.VerticalSplit.ToString();
    }
}
