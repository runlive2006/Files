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
    private readonly IDirectoryLister _lister = new Win32DirectoryLister();
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

        _settings = new SettingsService(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Files",
            "settings.json"));
        _settings.Load();

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
        SelectLayoutButton(mode);

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
            string path = settings.Panes[i].Path;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                panes[i].ActiveTab.Path = path;
            }

            if (Enum.TryParse(settings.Panes[i].ViewMode, ignoreCase: true, out ViewMode viewMode))
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

    private PaneViewModel GetViewModel(PaneState pane)
    {
        if (_viewModels.TryGetValue(pane, out PaneViewModel? existing))
        {
            return existing;
        }

        var viewModel = new PaneViewModel(pane, _lister);
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
            view.Initialize(GetViewModel(paneNode.Pane));
            view.PaneActivated += (_, _) =>
            {
                int index = _paneViews.IndexOf(view);
                if (index >= 0)
                {
                    SetFocusedIndex(index);
                }
            };

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
        SyncViewModeBox();
        UpdatePaneIndicator();
    }

    private void UpdatePaneIndicator() =>
        PaneIndicatorText.Text = $"栏 {_focusedIndex + 1}/{Math.Max(1, _paneViews.Count)}";

    private void SyncViewModeBox()
    {
        if (_focusedIndex < 0 || _focusedIndex >= _paneViews.Count)
        {
            return;
        }

        ViewMode mode = _paneViews[_focusedIndex].ViewModel.ViewMode;
        string tag = mode == ViewMode.List ? "List" : "Details";

        foreach (ComboBoxItem item in ViewModeBox.Items)
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                ViewModeBox.SelectedItem = item;
                break;
            }
        }
    }

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
        SelectLayoutButton(target);

        _ = NavigateAllPanesAsync();
    }

    private void SelectLayoutButton(PaneLayout mode)
    {
        LayoutSingleButton.IsEnabled = mode != PaneLayout.Single;
        LayoutHorizontalButton.IsEnabled = mode != PaneLayout.HorizontalSplit;
        LayoutVerticalButton.IsEnabled = mode != PaneLayout.VerticalSplit;
        LayoutQuadButton.IsEnabled = mode != PaneLayout.Quad;
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
        }
    }

    // ---------- 全局命令（作用于焦点栏） ----------

    private PaneView? FocusedPane =>
        _focusedIndex >= 0 && _focusedIndex < _paneViews.Count ? _paneViews[_focusedIndex] : null;

    private void OnNewFolderClick(object sender, RoutedEventArgs e) => _ = FocusedPane?.NewFolderAsync();

    private void OnRenameClick(object sender, RoutedEventArgs e) => _ = FocusedPane?.RenameAsync();

    private void OnDeleteClick(object sender, RoutedEventArgs e) => _ = FocusedPane?.DeleteAsync(DeleteMode.Recycle);

    private void OnViewModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModeBox.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        ViewMode mode = string.Equals(tag, "List", StringComparison.OrdinalIgnoreCase)
            ? ViewMode.List
            : ViewMode.Details;

        PaneView? pane = FocusedPane;
        if (pane is null)
        {
            return;
        }

        pane.ViewModel.ApplyViewMode(mode);
        pane.ApplyViewMode(mode);
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
            });
        }

        settings.SplitRatios.Clear();
        foreach (SplitNode split in LayoutFactory.CollectSplits(_layout))
        {
            settings.SplitRatios.Add(split.FirstRatio);
        }

        settings.LastPath = _paneViews.Count > 0
            ? _paneViews[_focusedIndex].ViewModel.CurrentPath
            : string.Empty;
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
