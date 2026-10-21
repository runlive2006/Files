namespace Files.Core.Models;

/// <summary>
/// 栏的布局方式。
/// </summary>
public enum PaneLayout
{
    /// <summary>单栏。</summary>
    Single,

    /// <summary>左右分栏。</summary>
    HorizontalSplit,

    /// <summary>上下分栏。</summary>
    VerticalSplit,

    /// <summary>田字四栏。</summary>
    Quad,
}

/// <summary>
/// 单个标签的浏览状态。这是"冷状态"：不持有任何视图或项容器，
/// 因此非活动标签可以廉价地长期保留（目标 ≤2KB/标签）。
/// </summary>
public sealed class TabState
{
    /// <summary>当前目录的完整路径。</summary>
    public required string Path { get; set; }

    /// <summary>视图模式。</summary>
    public ViewMode ViewMode { get; set; } = ViewMode.Details;

    /// <summary>排序依据。</summary>
    public string SortColumn { get; set; } = "Name";

    /// <summary>是否降序。</summary>
    public bool SortDescending { get; set; }

    /// <summary>滚动位置（像素，便于虚拟化还原）。</summary>
    public double ScrollOffset { get; set; }

    /// <summary>已选中的条目路径。</summary>
    public List<string> SelectedPaths { get; init; } = [];

    /// <summary>后退栈，最多保留 <see cref="HistoryDepth"/> 项。</summary>
    public List<string> BackStack { get; init; } = [];

    /// <summary>前进栈。</summary>
    public List<string> ForwardStack { get; init; } = [];

    /// <summary>历史栈深度上限。</summary>
    public const int HistoryDepth = 50;
}

/// <summary>
/// 视图模式。
/// </summary>
public enum ViewMode
{
    /// <summary>详情列表。</summary>
    Details,

    /// <summary>大图标。</summary>
    LargeIcons,

    /// <summary>小图标。</summary>
    SmallIcons,

    /// <summary>列表。</summary>
    List,

    /// <summary>平铺。</summary>
    Tiles,

    /// <summary>缩略图（大图标网格）。</summary>
    Thumbnails,
}

/// <summary>
/// 一个栏：持有若干标签，其中一个是活动标签。
/// </summary>
/// <remarks>
/// 这里承载标签集合的增删与活动索引维护。索引调整规则容易写错，
/// 因此集中在模型里并用单元测试锁定（见 <c>PaneStateTests</c>）。
/// </remarks>
public sealed class PaneState
{
    /// <summary>该栏的标签集合，至少包含一个。</summary>
    public required List<TabState> Tabs { get; init; }

    /// <summary>活动标签的下标。</summary>
    public int ActiveTabIndex { get; set; }

    /// <summary>活动标签；集合保证非空。</summary>
    public TabState ActiveTab
    {
        get
        {
            if (Tabs.Count == 0)
            {
                throw new InvalidOperationException("栏中至少应有一个标签。");
            }

            // 防御越界：外部直接改 ActiveTabIndex 时不应崩溃
            int index = Math.Clamp(ActiveTabIndex, 0, Tabs.Count - 1);
            return Tabs[index];
        }
    }

    /// <summary>
    /// 在活动标签之后插入新标签，并把它设为活动标签。
    /// </summary>
    public TabState AddTab(TabState tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        int insertAt = Math.Clamp(ActiveTabIndex + 1, 0, Tabs.Count);
        Tabs.Insert(insertAt, tab);
        ActiveTabIndex = insertAt;
        return tab;
    }

    /// <summary>
    /// 关闭指定标签。至少保留一个标签，因此关闭最后一个会返回 <see langword="false"/>。
    /// </summary>
    /// <returns>是否真的关闭了。</returns>
    public bool CloseTab(int index)
    {
        if (Tabs.Count <= 1 || index < 0 || index >= Tabs.Count)
        {
            return false;
        }

        Tabs.RemoveAt(index);

        if (index < ActiveTabIndex)
        {
            // 关掉的是活动标签左边的一个，活动标签整体左移
            ActiveTabIndex--;
        }
        else if (index == ActiveTabIndex)
        {
            // 关掉的是活动标签：由右邻接替；若它原本是最后一个，则退到新的最后一个
            if (ActiveTabIndex >= Tabs.Count)
            {
                ActiveTabIndex = Tabs.Count - 1;
            }
        }

        ActiveTabIndex = Math.Clamp(ActiveTabIndex, 0, Tabs.Count - 1);
        return true;
    }

    /// <summary>激活指定标签。</summary>
    public void Activate(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Tabs.Count);

        ActiveTabIndex = index;
    }

    /// <summary>把活动标签移到另一栏。</summary>
    /// <remarks>
    /// 只有当前栏还有别的标签时才允许移出，避免出现空栏。
    /// </remarks>
    public bool TryDetachActiveTab(out TabState? detached)
    {
        detached = null;

        if (Tabs.Count <= 1)
        {
            return false;
        }

        detached = ActiveTab;
        Tabs.RemoveAt(ActiveTabIndex);
        ActiveTabIndex = Math.Clamp(ActiveTabIndex, 0, Tabs.Count - 1);
        return true;
    }
}

/// <summary>
/// 布局树节点：要么是分裂（两个子节点），要么是叶子（一个栏）。
/// </summary>
public abstract class LayoutNode
{
    /// <summary>是否为叶子节点。</summary>
    public abstract bool IsLeaf { get; }
}

/// <summary>叶子节点，承载一个栏。</summary>
public sealed class PaneNode : LayoutNode
{
    /// <summary>该叶子的栏状态。</summary>
    public required PaneState Pane { get; init; }

    /// <inheritdoc />
    public override bool IsLeaf => true;
}

/// <summary>分裂节点，沿指定方向切分两个子节点。</summary>
public sealed class SplitNode : LayoutNode
{
    /// <summary>分裂方向。</summary>
    public required SplitDirection Direction { get; init; }

    /// <summary>第一个子节点（左/上）。</summary>
    public required LayoutNode First { get; init; }

    /// <summary>第二个子节点（右/下）。</summary>
    public required LayoutNode Second { get; init; }

    /// <summary>第一个子节点占用的比例（0..1）。</summary>
    public double FirstRatio { get; set; } = 0.5;

    /// <inheritdoc />
    public override bool IsLeaf => false;
}

/// <summary>分裂方向。</summary>
public enum SplitDirection
{
    /// <summary>左右切分。</summary>
    Horizontal,

    /// <summary>上下切分。</summary>
    Vertical,
}
