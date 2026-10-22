using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Files.App.ViewModels;
using Files.Core.Models;
using Files.Core.Services;
using Files.Interop;

namespace Files.App.Views;

/// <summary>
/// 单栏视图：独立的导航、标签条、列表与文件操作。
/// </summary>
/// <remarks>
/// 整个栏只有<b>一个</b> <c>ListView</c>。切换标签时替换它的 <c>ItemsSource</c>，
/// 而不是为每个标签保留一套视图 —— 这是"非活动标签不占内存"的实现方式。
/// </remarks>
public partial class PaneView : UserControl
{
    private static readonly Brush FocusBrush = CreateFrozen(Color.FromRgb(0x00, 0x78, 0xD4));

    private readonly IFileOperator _fileOperator = new ShellFileOperator();
    private GridView? _detailsView;
    private DataTemplate? _listTemplate;
    private DataTemplate? _thumbnailTemplate;
    private ItemsPanelTemplate? _thumbnailsPanel;
    private IconProvider? _icons;
    private TabViewModel? _boundTab;
    private bool _switchingTab;

    /// <summary>创建栏视图。</summary>
    public PaneView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>本栏的视图模型。</summary>
    public PaneViewModel ViewModel { get; private set; } = null!;

    /// <summary>栏获得焦点时触发（供窗口更新焦点栏）。</summary>
    public event EventHandler? PaneActivated;

    /// <summary>绑定视图模型。</summary>
    public void Initialize(PaneViewModel viewModel, IconProvider icons)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(icons);

        ViewModel = viewModel;
        _icons = icons;
        DataContext = viewModel;

        _detailsView = (GridView)EntriesList.View;
        _listTemplate = (DataTemplate)FindResource("ListTemplate");
        _thumbnailTemplate = (DataTemplate)FindResource("ThumbnailTemplate");
        _thumbnailsPanel = (ItemsPanelTemplate)FindResource("ThumbnailsPanel");

        AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnColumnHeaderClick));
        EntriesList.PreviewMouseRightButtonDown += OnListRightButtonDown;

        TabStrip.ItemsSource = viewModel.Tabs;
        viewModel.TabsChanged += (_, _) => UpdateTabStripVisibility();
        viewModel.PropertyChanged += OnPanePropertyChanged;

        BindActiveTab();
        UpdateTabStripVisibility();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            AddressBox.Text = ViewModel.ActiveTab.CurrentPath;
            RestoreScrollAndSelection();
        }
    }

    // ---------- 标签 ----------

    private void OnPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PaneViewModel.ActiveTab):
                BindActiveTab();
                break;

            case nameof(PaneViewModel.IsFocused):
                FocusBorder.BorderBrush = ViewModel.IsFocused ? FocusBrush : Brushes.Transparent;
                break;
        }
    }

    /// <summary>把界面绑定到当前活动标签。</summary>
    private void BindActiveTab()
    {
        if (_boundTab is not null)
        {
            _boundTab.PropertyChanged -= OnTabPropertyChanged;
        }

        TabViewModel tab = ViewModel.ActiveTab;
        _boundTab = tab;
        tab.PropertyChanged += OnTabPropertyChanged;

        // 只替换数据源，让 WPF 回收上一标签的项容器
        _switchingTab = true;
        EntriesList.ItemsSource = tab.Rows;
        _switchingTab = false;

        ApplyViewMode(tab.ViewMode);
        AddressBox.Text = tab.AddressText.Length > 0 ? tab.AddressText : tab.CurrentPath;
        UpdateNavigationState();

        // 布局完成后恢复滚动位置与选择集
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RestoreScrollAndSelection);
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _boundTab))
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(TabViewModel.AddressText):
                // 地址栏获得焦点时不打断用户输入
                if (!AddressBox.IsKeyboardFocusWithin)
                {
                    AddressBox.Text = ViewModel.ActiveTab.AddressText;
                }

                break;

            case nameof(TabViewModel.CanGoBack):
            case nameof(TabViewModel.CanGoForward):
            case nameof(TabViewModel.CurrentPath):
                UpdateNavigationState();
                break;

            case nameof(TabViewModel.Title):
                ViewModel.RefreshTitles();
                break;
        }
    }

    private void UpdateTabStripVisibility() =>
        TabStrip.Visibility = ViewModel.ShowTabStrip ? Visibility.Visible : Visibility.Collapsed;

    private void OnNewTabClick(object sender, RoutedEventArgs e) => _ = AddTabAsync();

    /// <summary>
    /// 点击标签正文切换活动标签。
    /// </summary>
    /// <remarks>
    /// 标签条用的是 <see cref="ItemsControl"/> 而不是 <c>ListBox</c>：
    /// 后者会带上一整套选中态外观与焦点行为，与自定义标签样式冲突。
    /// 代价是必须自己处理点击 —— 这正是"点击标签无法切换"的原因。
    /// </remarks>
    private void OnTabMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TabViewModel tab })
        {
            return;
        }

        int index = ViewModel.Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        if (ViewModel.ActiveIndex != index)
        {
            ViewModel.ActiveIndex = index;
        }

        // 兜底：即便索引未变化（例如关闭标签后索引恰好相同），也确保视图已重新绑定
        BindActiveTab();
        e.Handled = true;
    }

    private void OnCloseTabClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (((FrameworkElement)sender).DataContext is TabViewModel tab)
        {
            ViewModel.CloseTab(tab);
            RefreshTitlesSafe();
        }
    }

    /// <summary>新建标签并导航到当前路径。</summary>
    public async Task AddTabAsync()
    {
        TabViewModel tab = ViewModel.AddTab();
        ViewModel.ActiveIndex = ViewModel.Tabs.IndexOf(tab);

        RefreshTitlesSafe();
        BindActiveTab();
        await tab.NavigateAsync(tab.CurrentPath);
    }

    /// <summary>关闭当前标签。</summary>
    public bool CloseActiveTab()
    {
        bool closed = ViewModel.CloseActiveTab();
        if (closed)
        {
            RefreshTitlesSafe();
            BindActiveTab();
        }

        return closed;
    }

    /// <summary>切换到下一个 / 上一个标签。</summary>
    public void CycleTab(int delta)
    {
        ViewModel.CycleTab(delta);
        BindActiveTab();
    }

    /// <summary>恢复最近关闭的标签。</summary>
    public async Task ReopenClosedTabAsync()
    {
        TabViewModel? tab = ViewModel.ReopenClosedTab();
        if (tab is null)
        {
            return;
        }

        RefreshTitlesSafe();
        BindActiveTab();
        await tab.NavigateAsync(tab.CurrentPath);
    }

    private void RefreshTitlesSafe()
    {
        try
        {
            ViewModel.RefreshTitles();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            // 标题计算失败不应影响交互
        }
    }

    // ---------- 滚动与选择 ----------

    /// <summary>保存当前标签的滚动位置与选择集。</summary>
    public void CaptureTabViewState()
    {
        if (_boundTab is null || _switchingTab)
        {
            return;
        }

        ScrollViewer? scrollViewer = FindScrollViewer(EntriesList);
        if (scrollViewer is not null)
        {
            _boundTab.CaptureScrollOffset(scrollViewer.VerticalOffset);
        }

        var selected = EntriesList.SelectedItems
            .Cast<EntryRow>()
            .Select(row => row.FullPath);

        _boundTab.CaptureSelection(selected);
    }

    private void RestoreScrollAndSelection()
    {
        if (_boundTab is null)
        {
            return;
        }

        // 选择集恢复：按路径匹配（列表项数量可达十万级，但只做一次线性扫描）
        if (_boundTab.State.SelectedPaths.Count > 0)
        {
            var wanted = new HashSet<string>(_boundTab.State.SelectedPaths, StringComparer.OrdinalIgnoreCase);
            EntriesList.SelectedItems.Clear();

            foreach (EntryRow row in _boundTab.Rows)
            {
                if (wanted.Contains(row.FullPath))
                {
                    EntriesList.SelectedItems.Add(row);
                }
            }
        }

        if (_boundTab.State.ScrollOffset > 0)
        {
            ScrollViewer? scrollViewer = FindScrollViewer(EntriesList);
            scrollViewer?.ScrollToVerticalOffset(_boundTab.State.ScrollOffset);
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer)
        {
            return viewer;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            ScrollViewer? found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// 点击本栏任意位置都切换焦点栏。
    /// </summary>
    /// <remarks>
    /// 只依赖 <c>GotKeyboardFocus</c> 不够：若焦点已经在本栏内部，
    /// 再点击本栏的空白处不会触发键盘焦点事件，栏间切换会显得"点不动"。
    /// 因此在最外层 Border 上监听鼠标按下，覆盖标题、地址栏、标签条、列表空白区。
    /// </remarks>
    private void OnPanePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        PaneActivated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>替换图标提供者（设置变更时调用），并同步到所有标签。</summary>
    public void ReplaceIconProvider(IconProvider icons)
    {
        ArgumentNullException.ThrowIfNull(icons);
        _icons = icons;
        ViewModel.ReplaceIconProvider(icons);
    }
    /// <summary>图标设置变化后重建行（图标在行构造时确定，无法就地切换）。</summary>
    public async Task ReloadIconsAsync()
    {
        await ViewModel.ActiveTab.RefreshAsync();
        ApplyViewMode(ViewModel.ActiveTab.ViewMode);
    }
    // ---------- 焦点 ----------

    /// <inheritdoc />
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        PaneActivated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>让本栏的列表获得焦点。</summary>
    public void FocusList() => EntriesList.Focus();

    // ---------- 导航 ----------

    private void UpdateNavigationState()
    {
        BackButton.IsEnabled = ViewModel.ActiveTab.CanGoBack;
        ForwardButton.IsEnabled = ViewModel.ActiveTab.CanGoForward;
        UpButton.IsEnabled = System.IO.Directory.GetParent(ViewModel.ActiveTab.CurrentPath) is not null;
    }

    private async void OnBackClick(object sender, RoutedEventArgs e) =>
        await ViewModel.ActiveTab.GoBackAsync();

    private async void OnForwardClick(object sender, RoutedEventArgs e) =>
        await ViewModel.ActiveTab.GoForwardAsync();

    private async void OnUpClick(object sender, RoutedEventArgs e) =>
        await ViewModel.ActiveTab.GoUpAsync();

    private void OnAddressKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string target = PathUtility.Expand(AddressBox.Text);
            if (string.IsNullOrWhiteSpace(target))
            {
                AddressBox.Text = ViewModel.ActiveTab.CurrentPath;
                return;
            }

            e.Handled = true;
            _ = ViewModel.ActiveTab.NavigateAsync(target);
        }
        else if (e.Key == Key.Escape)
        {
            AddressBox.Text = ViewModel.ActiveTab.CurrentPath;
            EntriesList.Focus();
            e.Handled = true;
        }
    }

    private void OnAddressGotFocus(object sender, KeyboardFocusChangedEventArgs e) => AddressBox.SelectAll();

    private void OnAddressLostFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        AddressBox.Text = ViewModel.ActiveTab.CurrentPath;

    /// <summary>
    /// 双击条目：文件夹进入，文件用系统默认程序打开。
    /// </summary>
    private void OnEntryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EntriesList.SelectedItem is not EntryRow row)
        {
            return;
        }

        if (row.IsDirectory)
        {
            _ = ViewModel.ActiveTab.NavigateAsync(row.FullPath);
            return;
        }

        OpenWithShell(row.FullPath);
    }

    /// <summary>用系统关联程序打开文件。</summary>
    private void OpenWithShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            ViewModel.SetStatus($"无法打开：{ex.Message}");
        }
    }

    // ---------- 排序与视图 ----------

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

        TabViewModel tab = ViewModel.ActiveTab;
        bool descending = parsed == tab.SortColumn && !tab.SortDescending;
        tab.ApplySort(parsed, descending);
    }

    /// <summary>应用视图模式。</summary>
    public void ApplyViewMode(ViewMode mode)
    {
        switch (mode)
        {
            case ViewMode.List:
                EntriesList.View = null;
                EntriesList.ItemTemplate = _listTemplate;
                RestoreDefaultItemsPanel();
                break;

            case ViewMode.Thumbnails:
                EntriesList.View = null;
                EntriesList.ItemTemplate = _thumbnailTemplate;

                // 网格布局：换成可虚拟化的换行面板，否则大目录会一次性建满容器。
                // 模板定义在 XAML 中，运行时构造的模板不会被正确应用。
                EntriesList.ItemsPanel = _thumbnailsPanel;
                break;

            default:
                EntriesList.View = _detailsView;
                EntriesList.ItemTemplate = null;
                RestoreDefaultItemsPanel();
                break;
        }
    }

    /// <summary>
    /// 恢复 <see cref="ItemsControl.ItemsPanel"/> 的默认值。
    /// </summary>
    /// <remarks>
    /// <b>不能用 <c>ItemsPanel = null</c></b>：ListView 的默认面板（<c>VirtualizingStackPanel</c>）
    /// 定义在主题样式里，显式赋 null 会覆盖该样式值，导致 ItemsPresenter 找不到面板，
    /// 列表中<b>一条都不渲染</b>。必须用 <see cref="DependencyObject.ClearValue"/>
    /// 把本地值清掉，让样式值重新生效。
    /// </remarks>
    private void RestoreDefaultItemsPanel() =>
        EntriesList.ClearValue(ItemsControl.ItemsPanelProperty);

    // ---------- 文件操作 ----------

    private Window? OwnerWindow => Window.GetWindow(this);

    private IntPtr OwnerHandle => OwnerWindow is null
        ? IntPtr.Zero
        : new System.Windows.Interop.WindowInteropHelper(OwnerWindow).Handle;

    private List<EntryRow> SelectedRows => EntriesList.SelectedItems.Cast<EntryRow>().ToList();

    private IReadOnlyList<string> SelectedPaths => SelectedRows.Select(r => r.FullPath).ToList();

    private void OnListRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var container = ItemsControl.ContainerFromElement(EntriesList, (DependencyObject)e.OriginalSource)
            as ListViewItem;

        if (container is null || container.IsSelected)
        {
            return;
        }

        EntriesList.SelectedItems.Clear();
        container.IsSelected = true;
    }

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        bool hasSelection = EntriesList.SelectedItems.Count > 0;
        bool hasClipboard = FileClipboard.TryGetFiles(out _, out _);

        MenuOpen.IsEnabled = hasSelection;
        MenuRename.IsEnabled = hasSelection;
        MenuCopy.IsEnabled = hasSelection;
        MenuCut.IsEnabled = hasSelection;
        MenuDelete.IsEnabled = hasSelection;
        MenuPermanentDelete.IsEnabled = hasSelection;
        MenuCopyPath.IsEnabled = hasSelection;
        MenuPaste.IsEnabled = hasClipboard;
    }

    /// <summary>新建文件夹。</summary>
    public async Task NewFolderAsync()
    {
        TabViewModel tab = ViewModel.ActiveTab;
        if (string.IsNullOrEmpty(tab.CurrentPath))
        {
            return;
        }

        string suggested = UniqueNameGenerator.Generate(tab.CurrentPath, "新建文件夹");
        var dialog = new TextInputDialog("新建文件夹", "文件夹名称：", suggested, ValidateNewName)
        {
            Owner = OwnerWindow,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string parent = tab.CurrentPath;
        string name = dialog.Value;

        FileOperationResult result = await Task.Run(() => _fileOperator.CreateDirectory(parent, name));
        await HandleResultAsync(result, $"已新建文件夹：{name}");
    }

    /// <summary>重命名选中项。</summary>
    public async Task RenameAsync()
    {
        List<EntryRow> rows = SelectedRows;

        if (rows.Count != 1)
        {
            ViewModel.SetStatus(rows.Count == 0 ? "请先选择要重命名的项目。" : "一次只能重命名一个项目。");
            return;
        }

        EntryRow row = rows[0];
        var dialog = new TextInputDialog("重命名", "新名称：", row.Name, ValidateNewName)
        {
            Owner = OwnerWindow,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string path = row.FullPath;
        string newName = dialog.Value;

        FileOperationResult result = await Task.Run(() => _fileOperator.Rename(path, newName));
        await HandleResultAsync(result, $"已重命名为：{newName}");
    }

    /// <summary>删除选中项。</summary>
    public async Task DeleteAsync(DeleteMode mode)
    {
        List<EntryRow> rows = SelectedRows;
        if (rows.Count == 0)
        {
            ViewModel.SetStatus("请先选择要删除的项目。");
            return;
        }

        string message = mode == DeleteMode.Recycle
            ? $"确定要将这 {rows.Count} 个项目移到回收站吗？"
            : $"确定要永久删除这 {rows.Count} 个项目吗？\n\n此操作无法撤销。";

        if (!Confirm(message))
        {
            return;
        }

        List<string> paths = rows.Select(r => r.FullPath).ToList();
        IntPtr owner = OwnerHandle;

        FileOperationResult result = await Task.Run(() => _fileOperator.Delete(paths, mode, owner));

        await HandleResultAsync(
            result,
            mode == DeleteMode.Recycle
                ? $"已移到回收站：{rows.Count} 项"
                : $"已永久删除：{rows.Count} 项");
    }

    /// <summary>复制选中项到剪贴板。</summary>
    public void CopySelection() => SetClipboard(isCut: false);

    /// <summary>剪切选中项到剪贴板。</summary>
    public void CutSelection() => SetClipboard(isCut: true);

    /// <summary>粘贴。</summary>
    public async Task PasteAsync()
    {
        TabViewModel tab = ViewModel.ActiveTab;
        if (string.IsNullOrEmpty(tab.CurrentPath))
        {
            return;
        }

        if (!FileClipboard.TryGetFiles(out IReadOnlyList<string> sources, out bool isCut))
        {
            ViewModel.SetStatus("剪贴板中没有可粘贴的文件。");
            return;
        }

        string destination = tab.CurrentPath;
        IntPtr owner = OwnerHandle;

        FileOperationResult result = await Task.Run(() => isCut
            ? _fileOperator.Move(sources, destination, owner)
            : _fileOperator.Copy(sources, destination, owner));

        if (result.Success && isCut)
        {
            // 剪切粘贴成功后清空剪贴板，避免再次粘贴变成重复移动
            FileClipboard.Clear();
        }

        await HandleResultAsync(result, $"{(isCut ? "已移动" : "已复制")} {sources.Count} 项");
    }

    /// <summary>打开选中项。</summary>
    public void OpenSelection()
    {
        List<EntryRow> rows = SelectedRows;
        if (rows.Count == 0)
        {
            return;
        }

        if (rows.Count == 1 && rows[0].IsDirectory)
        {
            _ = ViewModel.ActiveTab.NavigateAsync(rows[0].FullPath);
            return;
        }

        foreach (EntryRow row in rows.Where(r => !r.IsDirectory))
        {
            try
            {
                Process.Start(new ProcessStartInfo(row.FullPath) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.ComponentModel.Win32Exception)
            {
                ViewModel.SetStatus($"无法打开：{ex.Message}");
            }
        }
    }

    /// <summary>复制选中项的路径到剪贴板。</summary>
    public void CopySelectionPath()
    {
        IReadOnlyList<string> paths = SelectedPaths;
        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, paths));
            ViewModel.SetStatus($"已复制 {paths.Count} 条路径");
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            ViewModel.SetStatus("无法访问剪贴板，请稍后重试。");
        }
    }

    private void OnNewFolderClick(object sender, RoutedEventArgs e) => _ = NewFolderAsync();

    private void OnRenameClick(object sender, RoutedEventArgs e) => _ = RenameAsync();

    private void OnDeleteClick(object sender, RoutedEventArgs e) => _ = DeleteAsync(DeleteMode.Recycle);

    private void OnPermanentDeleteClick(object sender, RoutedEventArgs e) => _ = DeleteAsync(DeleteMode.Permanent);

    private void OnCopyClick(object sender, RoutedEventArgs e) => CopySelection();

    private void OnCutClick(object sender, RoutedEventArgs e) => CutSelection();

    private void OnPasteClick(object sender, RoutedEventArgs e) => _ = PasteAsync();

    private void OnOpenClick(object sender, RoutedEventArgs e) => OpenSelection();

    private void OnCopyPathClick(object sender, RoutedEventArgs e) => CopySelectionPath();

    private void SetClipboard(bool isCut)
    {
        IReadOnlyList<string> paths = SelectedPaths;
        if (paths.Count == 0)
        {
            ViewModel.SetStatus("请先选择项目。");
            return;
        }

        ViewModel.SetStatus(FileClipboard.SetFiles(paths, isCut)
            ? $"{(isCut ? "已剪切" : "已复制")} {paths.Count} 项"
            : "无法访问剪贴板，可能被其他程序占用，请稍后重试。");
    }

    private async Task HandleResultAsync(FileOperationResult result, string successMessage)
    {
        if (result.Success)
        {
            ViewModel.SetStatus(successMessage);
            await ViewModel.RefreshAsync();
            return;
        }

        ViewModel.SetStatus($"操作失败：{result.ErrorMessage}");
        MessageBox.Show(
            OwnerWindow!,
            result.ErrorMessage ?? "未知错误",
            "操作失败",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private bool Confirm(string message) =>
        MessageBox.Show(OwnerWindow!, message, "Files", MessageBoxButton.YesNo, MessageBoxImage.Warning)
        == MessageBoxResult.Yes;

    private string? ValidateNewName(string name)
    {
        if (!PathUtility.IsValidFileName(name))
        {
            return "名称无效：不能包含 \\ / : * ? \" < > |，不能以点结尾，也不能是 CON、NUL 等保留名。";
        }

        return UniqueNameGenerator.Exists(ViewModel.ActiveTab.CurrentPath, name) ? "该名称已存在。" : null;
    }

    // ---------- 拖放 ----------

    /// <summary>拖拽起始点，用于判断是否超过系统拖动阈值。</summary>
    private Point _dragOrigin;

    /// <summary>是否已具备发起拖拽的条件（按在某个条目上）。</summary>
    private bool _dragArmed;

    private void OnListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragOrigin = e.GetPosition(null);

        // 只有按在条目上才允许拖动；按在空白处应当用于框选/清空选择
        _dragArmed = ItemsControl.ContainerFromElement(EntriesList, (DependencyObject)e.OriginalSource)
            is ListViewItem;
    }

    private void OnListPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point current = e.GetPosition(null);
        if (Math.Abs(current.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        List<EntryRow> rows = SelectedRows;
        if (rows.Count == 0)
        {
            return;
        }

        _dragArmed = false;

        // 使用系统标准的 FileDrop 格式：既能拖到本程序的其他栏，
        // 也能直接拖到资源管理器，反之亦然。
        var files = new System.Collections.Specialized.StringCollection();
        foreach (EntryRow row in rows)
        {
            files.Add(row.FullPath);
        }

        var data = new DataObject();
        data.SetFileDropList(files);

        DragDrop.DoDragDrop(EntriesList, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            DropOverlay.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }

        e.Effects = ResolveDragEffect(e);
        DropOverlay.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void OnListDragLeave(object sender, DragEventArgs e) =>
        DropOverlay.Visibility = Visibility.Collapsed;

    private async void OnListDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] items || items.Length == 0)
        {
            return;
        }

        string targetDirectory = ResolveDropDirectory(e);
        if (string.IsNullOrEmpty(targetDirectory))
        {
            return;
        }

        DropAction action = ResolveDropAction(e, items[0], targetDirectory);

        // 拖到条目原本所在的目录没有意义，直接忽略
        string[] sources = [.. items.Where(path =>
            !string.Equals(
                Path.GetDirectoryName(path),
                targetDirectory.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase))];

        if (sources.Length == 0)
        {
            ViewModel.SetStatus("目标位置与源位置相同，已忽略。");
            return;
        }

        IntPtr owner = OwnerHandle;
        bool isCopy = action == DropAction.Copy;

        FileOperationResult result = await Task.Run(() => isCopy
            ? _fileOperator.Copy(sources, targetDirectory, owner)
            : _fileOperator.Move(sources, targetDirectory, owner));

        if (!result.Success)
        {
            ViewModel.SetStatus($"操作失败：{result.ErrorMessage}");
            MessageBox.Show(
                OwnerWindow!,
                result.ErrorMessage ?? "未知错误",
                "操作失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        string targetName = Path.GetFileName(targetDirectory.TrimEnd('\\'));
        ViewModel.SetStatus($"{(isCopy ? "已复制" : "已移动")} {sources.Length} 项 → {targetName}");
        await ViewModel.RefreshAsync();

        // 让其他栏也刷新：移动操作的源栏可能是本窗口的另一个栏
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>内容发生变化（供窗口刷新其他栏）。</summary>
    public event EventHandler? ContentChanged;

    /// <summary>解析拖放目标目录：悬停在文件夹上则用该文件夹，否则用当前目录。</summary>
    private string ResolveDropDirectory(DragEventArgs e)
    {
        var container = ItemsControl.ContainerFromElement(EntriesList, (DependencyObject)e.OriginalSource)
            as ListViewItem;

        if (container?.DataContext is EntryRow { IsDirectory: true } row)
        {
            return row.FullPath;
        }

        return ViewModel.ActiveTab.CurrentPath;
    }

    private DragDropEffects ResolveDragEffect(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] items || items.Length == 0)
        {
            return DragDropEffects.None;
        }

        DropAction action = ResolveDropAction(e, items[0], ResolveDropDirectory(e));
        return action == DropAction.Copy ? DragDropEffects.Copy : DragDropEffects.Move;
    }

    private static DropAction ResolveDropAction(DragEventArgs e, string sourcePath, string targetDirectory) =>
        DropPolicy.Resolve(
            controlPressed: (e.KeyStates & DragDropKeyStates.ControlKey) != 0,
            shiftPressed: (e.KeyStates & DragDropKeyStates.ShiftKey) != 0,
            sourcePath: sourcePath,
            targetDirectory: targetDirectory);

    // ---------- 键盘 ----------

    private void OnListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        ModifierKeys modifiers = Keyboard.Modifiers;

        if (e.Key == Key.A && modifiers == ModifierKeys.Control)
        {
            EntriesList.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.I && modifiers == ModifierKeys.Control)
        {
            InvertSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.N && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _ = NewFolderAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.T && modifiers == ModifierKeys.Control)
        {
            _ = AddTabAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.W && modifiers == ModifierKeys.Control)
        {
            CloseActiveTab();
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            _ = RenameAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            _ = DeleteAsync(modifiers == ModifierKeys.Shift ? DeleteMode.Permanent : DeleteMode.Recycle);
            e.Handled = true;
        }
        else if (e.Key == Key.C && modifiers == ModifierKeys.Control)
        {
            CopySelection();
            e.Handled = true;
        }
        else if (e.Key == Key.X && modifiers == ModifierKeys.Control)
        {
            CutSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.V && modifiers == ModifierKeys.Control)
        {
            _ = PasteAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            _ = ViewModel.RefreshAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            _ = ViewModel.ActiveTab.GoBackAsync();
            e.Handled = true;
        }
    }

    private void InvertSelection()
    {
        var selected = EntriesList.SelectedItems.Cast<EntryRow>().ToHashSet();

        foreach (EntryRow row in ViewModel.ActiveTab.Rows)
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

    private static Brush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
