using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Files.App.ViewModels;
using Files.Core.Services;

namespace Files.App.Views;

/// <summary>
/// 导航窗格（左侧树）。
/// </summary>
/// <remarks>
/// 窗口级控件，四栏共享一个实例（NAVIGATION-AND-PANES §5.2）：
/// 四栏各配一棵树会让可用面积归零。
/// <para>
/// 与文件列表的联动由 <see cref="NavigateRequested"/> 实现：
/// 点击树节点时通知窗口，由窗口决定作用于焦点栏。
/// </para>
/// </remarks>
public partial class NavigationPane : UserControl
{
    private readonly NavigationTreeBuilder _builder = new();
    private bool _suppressSelectionEvent;

    /// <summary>创建导航窗格。</summary>
    public NavigationPane()
    {
        InitializeComponent();

        RootNodes = [];
        DataContext = this;

        Loaded += (_, _) => EnsureRootLoaded();
    }

    /// <summary>根节点集合。</summary>
    public ObservableCollection<NavigationNodeViewModel> RootNodes { get; }

    /// <summary>请求导航到某路径（由点击树节点触发）。</summary>
    public event EventHandler<string>? NavigateRequested;

    /// <summary>首次显示时构建根节点。</summary>
    private void EnsureRootLoaded()
    {
        if (RootNodes.Count > 0)
        {
            return;
        }

        foreach (NavigationNode node in _builder.BuildRoot())
        {
            RootNodes.Add(new NavigationNodeViewModel(node, _builder));
        }
    }

    /// <summary>
    /// 让树定位到指定路径（外部导航后调用）。
    /// </summary>
    /// <remarks>
    /// 只尝试在<b>已加载的节点</b>里查找；不为了定位而把整棵树展开 ——
    /// 那会触发大量磁盘枚举，正是懒加载要避免的。
    /// 找到就展开父级并选中；找不到就静默返回。
    /// </remarks>
    public void RevealPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        NavigationNodeViewModel? match = FindLoadedNode(RootNodes, path, depth: 0);

        if (match is null)
        {
            return;
        }

        _suppressSelectionEvent = true;

        try
        {
            match.IsSelected = true;
        }
        finally
        {
            _suppressSelectionEvent = false;
        }
    }

    private NavigationNodeViewModel? FindLoadedNode(
        IEnumerable<NavigationNodeViewModel> nodes,
        string path,
        int depth)
    {
        // 限制递归深度：树可能很深，且只应匹配已加载的部分
        if (depth > 16)
        {
            return null;
        }

        foreach (NavigationNodeViewModel node in nodes)
        {
            if (node.IsPlaceholder || node.IsGroup)
            {
                continue;
            }

            if (string.Equals(node.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            // 只递归已加载的子节点（Children 里没有占位节点说明已加载）
            if (node.Children.Count > 0 && node.Children[0].IsPlaceholder)
            {
                continue;
            }

            NavigationNodeViewModel? found = FindLoadedNode(node.Children, path, depth + 1);

            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressSelectionEvent)
        {
            return;
        }

        if (e.NewValue is not NavigationNodeViewModel node || !node.IsNavigable || node.Path is null)
        {
            return;
        }

        NavigateRequested?.Invoke(this, node.Path);
    }
}
