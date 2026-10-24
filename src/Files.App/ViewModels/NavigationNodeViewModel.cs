using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Files.Core.Services;

namespace Files.App.ViewModels;

/// <summary>
/// 导航树节点的视图模型，负责懒加载子节点。
/// </summary>
/// <remarks>
/// 懒加载的关键：<see cref="Children"/> 初始为空并挂一个占位子节点，
/// 这样 <c>TreeView</c> 会显示展开箭头；真正展开时才枚举磁盘（NAVIGATION §2.2）。
/// <para>
/// 占位节点必须在展开时移除，否则会留下一个空行。
/// </para>
/// </remarks>
public sealed class NavigationNodeViewModel : INotifyPropertyChanged
{
    /// <summary>占位节点标记（仅用于让 TreeView 显示展开箭头）。</summary>
    /// <summary>占位节点使用的构建器（不会被真正使用）。</summary>
    private static readonly NavigationTreeBuilder SharedBuilder = new();

    private static readonly NavigationNodeViewModel Placeholder =
        new(new NavigationNode(NavigationNodeKind.Group, "加载中…", null, false), isPlaceholder: true);

    private readonly NavigationTreeBuilder _builder;
    private bool _isExpanded;
    private bool _isSelected;
    private bool _childrenLoaded;

    private NavigationNodeViewModel(NavigationNode node, bool isPlaceholder = false)
    {
        Node = node;
        IsPlaceholder = isPlaceholder;
        // 占位节点不会枚举子项，但仍传入共享构建器以保持一致
        _builder = SharedBuilder;
        Children = [];
    }

    /// <summary>创建可懒加载的节点。</summary>
    public NavigationNodeViewModel(NavigationNode node, NavigationTreeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(builder);

        Node = node;
        _builder = builder;
        Children = [];

        if (node.HasChildren && node.IsNavigable)
        {
            // 占位子节点：让 TreeView 显示展开箭头，展开时才真正枚举
            Children.Add(Placeholder);
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>对应的数据节点。</summary>
    public NavigationNode Node { get; }

    /// <summary>
    /// 返回显示名称。
    /// </summary>
    /// <remarks>
    /// 树节点的 UI Automation 名称在未显式设置时退化为 <c>ToString()</c>，
    /// 默认会输出类型全名，屏幕阅读器读不出有意义的内容。
    /// </remarks>
    public override string ToString() => DisplayName;
    /// <summary>是否为占位节点。</summary>
    public bool IsPlaceholder { get; }

    /// <summary>子节点。</summary>
    public ObservableCollection<NavigationNodeViewModel> Children { get; }

    /// <summary>显示名称。</summary>
    public string DisplayName => Node.DisplayName;

    /// <summary>对应路径。</summary>
    public string? Path => Node.Path;

    /// <summary>是否可导航（分组节点为 <see langword="false"/>）。</summary>
    public bool IsNavigable => Node.IsNavigable;

    /// <summary>是否为分组标题节点（界面上加粗、不可点击导航）。</summary>
    public bool IsGroup => Node.Kind == NavigationNodeKind.Group;

    /// <summary>
    /// 是否已展开。
    /// </summary>
    /// <remarks>
    /// <c>TreeViewItem.IsExpanded</c> 双向绑定到此属性；首次变为 <see langword="true"/> 时触发懒加载。
    /// </remarks>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();

            if (value)
            {
                EnsureChildrenLoaded();
            }
        }
    }

    /// <summary>是否被选中。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 枚举并填充子节点（幂等）。
    /// </summary>
    /// <remarks>
    /// 重复调用不会重建子节点 —— 保留已展开的子节点状态，
    /// 否则每次折叠再展开都会丢掉下层的展开位置。
    /// </remarks>
    public void EnsureChildrenLoaded()
    {
        if (_childrenLoaded || IsPlaceholder || !IsNavigable)
        {
            return;
        }

        _childrenLoaded = true;
        Children.Clear();

        if (Path is null)
        {
            return;
        }

        IReadOnlyList<NavigationNode> childNodes = _builder.GetChildFolders(Path);

        foreach (NavigationNode child in childNodes)
        {
            Children.Add(new NavigationNodeViewModel(child, _builder));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
