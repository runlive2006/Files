using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Files.Core.Services;

namespace Files.App.Views;

/// <summary>
/// 地址栏：面包屑与可编辑文本框双模式。
/// </summary>
/// <remarks>
/// 行为对齐资源管理器：
/// <list type="bullet">
/// <item>默认显示面包屑，每段可点击直接导航；</item>
/// <item>点击空白处（或按 <c>Alt+D</c>）切换为文本框，可粘贴路径；</item>
/// <item>文本框失焦或按 <c>Esc</c> 回到面包屑；</item>
/// <item>回车提交新路径。</item>
/// </list>
/// </remarks>
public partial class BreadcrumbBar : UserControl
{
    private static readonly Brush HoverBrush = CreateFrozen(Color.FromRgb(0xE8, 0xF0, 0xFA));
    private static readonly Brush SeparatorBrush = CreateFrozen(Color.FromRgb(0x99, 0x99, 0x99));

    private string _currentPath = string.Empty;
    private bool _suppressLostFocus;

    /// <summary>创建地址栏。</summary>
    public BreadcrumbBar()
    {
        InitializeComponent();
    }

    /// <summary>用户点击某个分段或提交新路径时触发，参数为要导航到的路径。</summary>
    public event EventHandler<string>? NavigateRequested;

    /// <summary>当前路径。</summary>
    public string CurrentPath => _currentPath;

    /// <summary>
    /// 设置当前路径并刷新面包屑。
    /// </summary>
    /// <param name="path">新路径。</param>
    /// <param name="rootDisplayName">根分段显示名（如卷标）；为空则用盘符。</param>
    public void SetPath(string? path, string? rootDisplayName = null)
    {
        _currentPath = path ?? string.Empty;

        if (EditBox.Visibility == Visibility.Visible)
        {
            // 编辑中不打断用户输入，只更新底层路径
            return;
        }

        RebuildCrumbs(rootDisplayName);
    }

    private void RebuildCrumbs(string? rootDisplayName)
    {
        CrumbPanel.Children.Clear();

        IReadOnlyList<BreadcrumbSegment> segments =
            BreadcrumbBuilder.Build(_currentPath, rootDisplayName);

        for (int i = 0; i < segments.Count; i++)
        {
            if (i > 0)
            {
                CrumbPanel.Children.Add(CreateSeparator());
            }

            CrumbPanel.Children.Add(CreateSegmentButton(segments[i]));
        }
    }

    private static TextBlock CreateSeparator() => new()
    {
        Text = "›",
        Foreground = SeparatorBrush,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(2, 0, 2, 0),
        FontSize = 12,
    };

    private Button CreateSegmentButton(BreadcrumbSegment segment)
    {
        var button = new Button
        {
            Content = segment.DisplayName,
            Tag = segment.FullPath,
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 220,
            ToolTip = segment.FullPath,
        };

        button.Click += OnSegmentClick;
        button.MouseEnter += (_, _) => button.Background = HoverBrush;
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;

        return button;
    }

    private void OnSegmentClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target } || string.IsNullOrEmpty(target))
        {
            return;
        }

        NavigateRequested?.Invoke(this, target);
    }

    // ---------- 编辑模式 ----------

    /// <summary>切换到文本编辑模式（点击空白处或 Alt+D 触发）。</summary>
    public void BeginEdit()
    {
        _suppressLostFocus = true;

        EditBox.Text = _currentPath;
        CrumbHost.Visibility = Visibility.Collapsed;
        EditBox.Visibility = Visibility.Visible;

        EditBox.Focus();
        EditBox.SelectAll();

        _suppressLostFocus = false;
    }

    /// <summary>回到面包屑模式。</summary>
    public void EndEdit()
    {
        EditBox.Visibility = Visibility.Collapsed;
        CrumbHost.Visibility = Visibility.Visible;
        RebuildCrumbs(null);
    }

    /// <summary>是否处于编辑模式。</summary>
    public bool IsEditing => EditBox.Visibility == Visibility.Visible;

    private void OnEditKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string target = PathUtility.Expand(EditBox.Text);

            if (string.IsNullOrWhiteSpace(target))
            {
                EndEdit();
                e.Handled = true;
                return;
            }

            EndEdit();
            NavigateRequested?.Invoke(this, target);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            EndEdit();
            e.Handled = true;
        }
    }

    private void OnEditLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_suppressLostFocus)
        {
            return;
        }

        EndEdit();
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        // 点击空白区域（没有落在分段按钮上）切换为编辑模式
        if (e.OriginalSource is Border || e.OriginalSource is ScrollViewer || e.OriginalSource is Grid)
        {
            BeginEdit();
        }
    }

    private static Brush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
