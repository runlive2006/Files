using Files.Core.Models;

namespace Files.Core.Services;

/// <summary>
/// 根据布局模式构造布局树，并实现 PRD §2.1 的四栏生成规则。
/// </summary>
public static class LayoutFactory
{
    /// <summary>
    /// 按目标布局构造新的布局树。
    /// </summary>
    /// <param name="current">当前布局树；为 <see langword="null"/> 时按单栏起步。</param>
    /// <param name="target">目标布局。</param>
    /// <param name="focusedPaneIndex">
    /// 当前焦点栏在 <see cref="CollectPanes"/> 顺序中的下标。
    /// 该栏的 <see cref="PaneState"/> 会被<b>原样复用</b>，从而保留它的路径、选择与标签，
    /// 其余新栏从它克隆（PRD §2.1："新栏继承来源栏的活动标签路径"）。
    /// </param>
    public static LayoutNode Create(LayoutNode? current, PaneLayout target, int focusedPaneIndex = 0)
    {
        var panes = current is null ? [] : CollectPanes(current).ToList();
        if (panes.Count == 0)
        {
            panes.Add(CreateDefaultPane());
        }

        int index = Math.Clamp(focusedPaneIndex, 0, panes.Count - 1);
        PaneState primary = panes[index];

        return target switch
        {
            PaneLayout.Single => Leaf(primary),
            PaneLayout.HorizontalSplit => Split(
                SplitDirection.Horizontal,
                Leaf(primary),
                Leaf(Clone(primary))),
            PaneLayout.VerticalSplit => Split(
                SplitDirection.Vertical,
                Leaf(primary),
                Leaf(Clone(primary))),
            // Quad：先左右分裂，再各自上下分裂，形成 2×2
            PaneLayout.Quad => Split(
                SplitDirection.Horizontal,
                Split(SplitDirection.Vertical, Leaf(primary), Leaf(Clone(primary))),
                Split(SplitDirection.Vertical, Leaf(Clone(primary)), Leaf(Clone(primary)))),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "未知的布局模式。"),
        };
    }

    /// <summary>收集布局树中的全部栏，按从左到右、从上到下的顺序。</summary>
    public static IReadOnlyList<PaneState> CollectPanes(LayoutNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var result = new List<PaneState>();
        Walk(root, result);
        return result;
    }

    /// <summary>统计布局树中的栏数量。</summary>
    public static int CountPanes(LayoutNode root) => CollectPanes(root).Count;

    /// <summary>收集布局树中的全部分裂节点，按遍历顺序（用于持久化分隔条比例）。</summary>
    public static IReadOnlyList<SplitNode> CollectSplits(LayoutNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var result = new List<SplitNode>();
        WalkSplits(root, result);
        return result;
    }

    private static void Walk(LayoutNode node, List<PaneState> sink)
    {
        switch (node)
        {
            case PaneNode pane:
                sink.Add(pane.Pane);
                break;
            case SplitNode split:
                Walk(split.First, sink);
                Walk(split.Second, sink);
                break;
            default:
                throw new NotSupportedException($"未知的布局节点类型：{node.GetType().Name}");
        }
    }

    private static void WalkSplits(LayoutNode node, List<SplitNode> sink)
    {
        if (node is not SplitNode split)
        {
            return;
        }

        sink.Add(split);
        WalkSplits(split.First, sink);
        WalkSplits(split.Second, sink);
    }

    private static PaneNode Leaf(PaneState state) => new() { Pane = state };

    private static PaneState CreateDefaultPane()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return new PaneState
        {
            Tabs = [new TabState { Path = string.IsNullOrEmpty(home) ? "C:\\" : home }],
            ActiveTabIndex = 0,
        };
    }

    /// <summary>从模板栏克隆出一个独立的新栏（只保留活动标签）。</summary>
    private static PaneState Clone(PaneState template) => new()
    {
        Tabs = [CloneTab(template.ActiveTab)],
        ActiveTabIndex = 0,
    };

    private static TabState CloneTab(TabState source) => new()
    {
        Path = source.Path,
        ViewMode = source.ViewMode,
        SortColumn = source.SortColumn,
        SortDescending = source.SortDescending,
        ScrollOffset = 0,
    };

    private static SplitNode Split(SplitDirection direction, LayoutNode first, LayoutNode second) => new()
    {
        Direction = direction,
        First = first,
        Second = second,
        FirstRatio = 0.5,
    };
}
